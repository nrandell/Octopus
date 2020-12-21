module MqttProcessing

open MQTTnet
open MQTTnet.Client.Options
open MQTTnet.Extensions.ManagedClient
open System
open Nick.Energy.Models
open NodaTime
open System.Text.Json
open NodaTime.Serialization.SystemTextJson
open NodaTime.Text

let mutable private mqttClient: IManagedMqttClient option = None

let jsonSerializerOptions = 
    let options = JsonSerializerOptions(PropertyNameCaseInsensitive = true, IgnoreNullValues = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false)
    options.Converters.Add(
        NodaPatternConverter<LocalDateTime>(
            LocalDateTimePattern.CreateWithInvariantCulture("yyyy-MM-dd HH:mm:ss")
        )
    )
    options.Converters.Add(
        NodaPatternConverter<Instant>(
            InstantPattern.CreateWithInvariantCulture("yyyy-MM-dd HH:mm:ss")
        )
    )

    options.Converters.Add(
        NodaPatternConverter<LocalTime>(
            LocalTimePattern.CreateWithInvariantCulture("HH:mm")
        )
    )
    options

let deserialize<'T> (msg: MqttApplicationMessage) =
    JsonSerializer.Deserialize<'T>(ReadOnlySpan<byte>.op_Implicit msg.Payload, jsonSerializerOptions)

let serialize msg =
    JsonSerializer.Serialize(msg, jsonSerializerOptions)




type PublishedMessage = {
    Timestamp: Instant
    Cheapest30Minutes: LocalTime
    Cheapest1Hour: LocalTime
    Cheapest2Hours: LocalTime
    Cheapest3Hours: LocalTime
    YesterdayCost: float<pence>
    TodayCost: float<pence>
    LastCost: float<pence>
}



let start (processor: MailboxProcessor<MqttApplicationMessage>) = async {
    let options = 
        ManagedMqttClientOptionsBuilder()
            .WithAutoReconnectDelay(TimeSpan.FromSeconds(5.0))
            .WithClientOptions(MqttClientOptionsBuilder()
                .WithClientId("octopus-live-server")
                .WithTcpServer("server.home")
                .Build()
            )
            .Build()

    let client = MqttFactory().CreateManagedMqttClient()
    client.UseApplicationMessageReceivedHandler(fun args -> processor.Post args.ApplicationMessage) |> ignore
    mqttClient <- Some client

    do! client.SubscribeAsync(MqttTopicFilterBuilder().WithTopic("nick/sensor/hildebrand/state").Build()) |> Async.AwaitTask
    do! client.StartAsync(options) |> Async.AwaitTask
}


let publish (msg: PublishedMessage) = 
    match mqttClient with
    | None -> 
        failwith "No client created"
    | Some client -> 
        async {
            let! ct = Async.CancellationToken
            let outgoingMessage = 
                MqttApplicationMessageBuilder()
                    .WithTopic("nick/sensor/energy/state")
                    .WithPayload(serialize msg)
                    .WithRetainFlag(true)
                    .Build()

            do! client.PublishAsync(outgoingMessage, ct) |> Async.AwaitTask |> Async.Ignore
        }

