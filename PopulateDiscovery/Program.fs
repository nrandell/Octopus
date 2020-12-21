// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp

open System
open System.Text.Json
open System.IO
open MQTTnet.Client.Options
open MQTTnet

let createClient (server:string) = async {
    let! ct = Async.CancellationToken
    let options = 
        MqttClientOptionsBuilder()
            .WithClientId("PopulateDiscovery")
            .WithTcpServer(server)
            .Build()

    let client = MqttFactory().CreateMqttClient()
    do! client.ConnectAsync(options, ct) |> Async.AwaitTask |> Async.Ignore
    return client
}
   


[<EntryPoint>]
let main argv =
    if argv.Length <> 2 then
        printfn "usage: PopulateDiscovery <mqtt server> <json>"
        -1
    else
        let server = argv.[0]
        let json = argv.[1]

        async {
            let! ct = Async.CancellationToken
            let! client = createClient server

            let! jsonContents = File.ReadAllTextAsync(json, ct) |> Async.AwaitTask
            let doc = JsonDocument.Parse jsonContents
            for o in doc.RootElement.EnumerateObject() do
                let name = o.Name
                let value = o.Value

                printfn $"{name}"

                let message = 
                    MqttApplicationMessageBuilder()
                        .WithTopic($"homeassistant/{name}/config")
                        .WithPayload(value.ToString())
                        .WithRetainFlag(true)
                        .Build()

                do! client.PublishAsync(message, ct) |> Async.AwaitTask |> Async.Ignore


        }
        |> Async.RunSynchronously

        0
