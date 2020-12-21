open System
open System.Threading.Tasks
open System.Threading
open MQTTnet
open NodaTime
open Nick.Energy.Models
open FSharp.Control


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
    

type state = 
    | Empty
    | Previous of HildebrandProcessedMessage*Total


let processor = 
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
                        TodayCost = total'.CurrentDayCost
                        YesterdayCost = total'.YesterdayCost
                        LastCost = total'.LastCost
                    }
                do! MqttProcessing.publish publish


                do! loop state'
            }
        loop Empty
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
    processor.Error.Add(fun e -> printfn $"Error: {e}")
    try
        async {
            do! MqttProcessing.start processor
            do! delay()
        }
        |> run
    with
        | :? OperationCanceledException -> printfn "Cancelled"
        | ex -> printfn $"Error: {ex.Message}"

    0 // return an integer exit code