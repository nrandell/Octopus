module Tariffs
open InfluxDB.Client
open NodaTime
open Nick.Energy.Models


let private client = 
    let options = 
        InfluxDBClientOptions
            .Builder
            .CreateNew()
            .Url("http://server.home:18086")
            .Org("home")
            .Bucket("octopus_official")
            .AuthenticateToken("dyHMkskjYEH8xZDnk9JQnqKCe-fSSl7a1pMsXaazbDRwsRhA0agBmi0IPEnatbJWTYjketIHMtTPiUylZj28HA==")
            .Build()
    InfluxDBClientFactory.Create(options)

let private queryApi = client.GetQueryApi()



   
let private secondsPerPeriod = 60L * 30L
let private periodDuration = Duration.FromSeconds secondsPerPeriod
let createTimePeriod (timestamp: Instant) =
    let seconds = timestamp.ToUnixTimeSeconds()
    seconds / secondsPerPeriod
    
    
let mutable private timePeriodMap = Map.empty

let private retrieveTariffFor period = async {
    let timestamp = Instant.FromUnixTimeSeconds (period * secondsPerPeriod)
    let query = $"""
from(bucket: "octopus_official")
|> range(start: {timestamp}, stop: {timestamp.Plus periodDuration})
|> filter(fn: (r) => (r._measurement == "Tariff"))
|> pivot(rowKey:["_time"], columnKey: ["_field"], valueColumn: "_value")
"""
    let! response = queryApi.QueryAsync<OctopusTariffEntry>(query) |> Async.AwaitTask
    let result = 
        match response.Count with
        | 0 -> None
        | 1 -> Some response.[0]
        | n -> failwith $"Expected 1 or 0 results, got {n}"
    return result
}

        
let getTariffFor (timestamp: Instant) = async {
    let period = createTimePeriod timestamp
    match timePeriodMap.TryGetValue period with
    | true, tariff -> return tariff
    | _ ->
        let! newTariff = retrieveTariffFor period
        timePeriodMap <- timePeriodMap.Add (period, newTariff)
        return newTariff
}

