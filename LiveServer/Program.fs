open System
open System.Threading.Tasks
open System.Threading
open MQTTnet
open NodaTime
open Nick.Energy.Models
open FSharp.Control
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open NodaTime.Serialization.SystemTextJson

let options =
    let o = JsonSerializerOptions()
    o.Converters.Add(JsonFSharpConverter())
    o.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb) |> ignore
    o

let serializeAsync (s:Stream) v ct = JsonSerializer.SerializeAsync(s, v, options, ct) |> Async.AwaitTask

let deserializeAsync<'T> (s:Stream) ct = JsonSerializer.DeserializeAsync<'T>(s, options, ct).AsTask() |> Async.AwaitTask


let roundForDashboard (v:float<pence>) = Math.Round((float)v, 2) * 1.0<pence>

type TimeCost = {
    Start: LocalTime
    Cost: float<pence/kWh>
}

type DayInformation = {
    Day: LocalDate
    CheapestPeriod: TimeCost
    CheapestHour: TimeCost
    Cheapest2Hours: TimeCost
    Cheapest3Hours: TimeCost
}

let getDayTarrifs (local: LocalDateTime) = asyncSeq {
    let startHour = if local.Hour < 6 || local.Hour > 20 then 6 else local.Hour
    let day = 
        if local.Hour > 20 then
            local.Date.PlusDays 1
        else
            local.Date

    let firstCandidate = LocalTime(startHour, 0, 0)
    let lastCandidate = LocalTime(22, 30, 0)
    let mutable time = firstCandidate
    while time <= lastCandidate do
        let todayTime = LocalDateTime(day.Year, day.Month, day.Day, time.Hour, time.Minute, time.Second)
        let instant = todayTime.InUtc().ToInstant()
        let! tarriff = Tariffs.getTariffFor instant
        yield (time, tarriff)
        time <- time.PlusMinutes 30L
}

let buildDayInformation (utcTimestamp: Instant) = async {
    let local = utcTimestamp.InUtc().LocalDateTime
    let day = local.Date
    let! candidateTimes = 
        getDayTarrifs local
        |> AsyncSeq.toListAsync 

    let findCheapest (periods: seq<LocalTime*float<pence/kWh>>) =
        periods
        |> Seq.minBy (fun (lt,p) -> p)


    let findCheapestConsecutive periods =
        candidateTimes
        |> Seq.windowed periods
        |> Seq.map (fun entries ->
            let price = 
                entries 
                |> Array.averageBy (fun (_,p) -> 
                    match p with
                    | Some t -> t.ValueExcVat * 1.0<pence/kWh>
                    | None -> 99.0<pence/kWh>
                    )
            let lt,_ = entries.[0]
            {Start = lt; Cost = price }
        )
        |> Seq.minBy (fun p -> p.Cost)

    return {
        Day = day
        CheapestPeriod = findCheapestConsecutive 1
        CheapestHour = findCheapestConsecutive 2
        Cheapest2Hours = findCheapestConsecutive 4
        Cheapest3Hours = findCheapestConsecutive 6
    }
}



type PeriodInformation = {
    Period: int64
    Price: float<pence/kWh>
    Start: int<wh>
}

type Total = {
    Today: DayInformation
    YesterdayCost: float<pence>
    CurrentDayCost: float<pence>
    LastCost: float<pence>
    LastUsed: float<kWh>
    CurrentPeriod: PeriodInformation
}
    

type State = 
    | Empty
    | Previous of HildebrandProcessedMessage*Total


let stateFileName = "state.json"

let private tidyState (stateFile: string) =
    let partialFile = stateFile + ".pt"
    let oldFile = stateFile + ".old"
    if File.Exists partialFile then File.Delete partialFile
    match File.Exists oldFile, File.Exists stateFile with
    | true, true ->
        File.Delete stateFile
        File.Move(oldFile, stateFile)
    | true, false ->
        File.Delete stateFile
    | false, _ -> ()



let saveJson fileName value = async {
    let! ct = Async.CancellationToken
    use stream = File.Create fileName
    do! serializeAsync stream value ct
}

let saveState (stateFile: string) (state: State) = async {
    tidyState stateFile


    let partialFile = stateFile + ".pt"
    let oldFile = stateFile + ".old"
    do! saveJson partialFile state 

    if File.Exists stateFile then File.Move(stateFile, oldFile)
    File.Move(partialFile, stateFile)
}

let loadState (stateFile: string) = async {
    tidyState stateFile
    let! ct = Async.CancellationToken

    if File.Exists stateFile then
        use stream = File.OpenRead stateFile
        let! state = deserializeAsync<State> stream ct
        return state
    else
        let state = Empty
        return state
}


let createProcessor (initialState: State) = 
    MailboxProcessor<MqttApplicationMessage>.Start(fun inbox -> 
        let rec loop state =
            async {
                let! message = inbox.Receive()
                //printfn $"Received on {message.Topic}"
                let msg = MqttProcessing.deserialize<HildebrandProcessedMessage> message
                let period = Tariffs.createTimePeriod msg.UtcTimestamp
                let day = msg.UtcTimestamp.InUtc().LocalDateTime.Date
                let! tariff = Tariffs.getTariffFor msg.UtcTimestamp
                let unitPrice = 
                    match tariff with
                    | Some t -> t.ValueExcVat * 1.0<pence/kWh>
                    | None -> 0.0<pence/kWh>

                let! total' =
                    match state with
                    | Empty -> 
                        async {
                            let! today = buildDayInformation msg.UtcTimestamp
                            return {
                                Today = today
                                YesterdayCost = 0.0<pence>
                                CurrentDayCost = 0.0<pence>
                                LastCost = 0.0<pence>
                                LastUsed = 0.0<kWh>
                                CurrentPeriod = 
                                    {
                                        Period = period
                                        Price = unitPrice
                                        Start = msg.WattHoursDelivered
                                    }
                            }
                        }
                    | Previous (previousMsg,total) -> async {
                        let used = float (msg.WattHoursDelivered - previousMsg.WattHoursDelivered) * 1.0<wh>
                        let usedKwh = wattHoursToKilowattHours used
                        let cost = usedKwh * unitPrice

                        if period = total.CurrentPeriod.Period then
                            return { 
                                total with
                                    LastCost = cost
                                    LastUsed = usedKwh
                            }
                        else
                            let periodUsed = float (msg.WattHoursDelivered - total.CurrentPeriod.Start) * 1.0<wh>
                            let periodKwh = wattHoursToKilowattHours periodUsed
                            let periodCost = periodKwh * total.CurrentPeriod.Price
                            let! today = buildDayInformation msg.UtcTimestamp
                            let todayCost, yesterdayCost =
                                let newCost = (total.CurrentDayCost + periodCost)
                                if day = total.Today.Day then
                                    newCost, total.YesterdayCost
                                else
                                    0.0<pence>, newCost

                            return {
                                Today = today
                                YesterdayCost = yesterdayCost
                                CurrentDayCost = todayCost
                                LastCost = cost
                                LastUsed = usedKwh
                                CurrentPeriod = 
                                    {
                                        Period = period
                                        Price = unitPrice
                                        Start = msg.WattHoursDelivered
                                    }
                            }
                    }
                        

                let state' = Previous(msg,total')
                //printfn $"{msg.UtcTimestamp}: {unitPrice} {total'.CurrentDayCost} {total'.LastCost} {total'.LastUsed} {total'.Today}"

                let publish : MqttProcessing.PublishedMessage = 
                    let today = total'.Today
                    {
                        Timestamp = msg.UtcTimestamp
                        Cheapest30Minutes = today.CheapestPeriod.Start
                        Cheapest1Hour = today.CheapestHour.Start
                        Cheapest2Hours = today.Cheapest2Hours.Start
                        Cheapest3Hours = today.Cheapest3Hours.Start
                        TodayCost =  roundForDashboard total'.CurrentDayCost
                        YesterdayCost = roundForDashboard total'.YesterdayCost
                        LastCost = roundForDashboard total'.LastCost
                    }
                do! MqttProcessing.publish publish

                do! saveState stateFileName state'
                do! loop state'
            }
        loop initialState
    )

let delay() = async {
    let! ct = Async.CancellationToken
    do! Task.Delay(Timeout.Infinite, ct) |> Async.AwaitTask
}

[<EntryPoint>]
let main argv =
    use cts = new CancellationTokenSource()
    Console.CancelKeyPress.Add(fun e -> 
        printfn "Cancelling"
        e.Cancel <- true
        cts.Cancel()
    )

    let run x = Async.RunSynchronously(x, cancellationToken = cts.Token)
    try
        async {
            let! state = loadState stateFileName
            let processor = createProcessor state
            processor.Error.Add(fun e -> printfn $"Error: {e}")

            do! MqttProcessing.start processor
            do! delay()
        }
        |> run
    with
        | :? OperationCanceledException -> printfn "Cancelled"
        | ex -> printfn $"Error: {ex.Message}"

    0 // return an integer exit code