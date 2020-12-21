module Nick.Energy.Models

open NodaTime


[<Measure>] type watt
[<Measure>] type kilowatt
[<Measure>] type hour
[<Measure>] type second
[<Measure>] type wh = watt / hour
[<Measure>] type kWh = kilowatt / hour
[<Measure>] type pence

let wattsPerKilowatt : float<watt/kilowatt> = 1000.0<watt/kilowatt>
let secondsPerHour : float<second/hour> = 3600.0<second/hour>

let wattHoursToKilowattHours (a: float<wh>): float<kWh> = a / wattsPerKilowatt



type HildebrandProcessedMessage = {
    UtcTimestamp: Instant
    HardwareVersion: string
    SiteId: string
    Rssi: int
    Lqi: int
    WattHoursDelivered: int<wh>
    CurrentWatts: int<watt>
    TodayWattHours: int<wh>
    ThisWeekWattHours: int<wh>
    ThisMonthWattHours: int<wh>
}
