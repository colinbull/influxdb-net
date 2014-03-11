open System
open InfluxDB

type Event =
    { User: string
      Action: string }


[<EntryPoint>]
let main _ = 

    let config =
        { Client =
              { MaxBufferSize = 50
                MaxBufferTime = 1000 }
          Server =
              { Host = "192.168.45.132"
                Port = 8086
                Database = "testdb"
                Username = "testuser" 
                Password = "testpass" } }

    let client = InfluxDBClient (config)

    client.Write ("testseries", { User = "andrew"; Action = "got lunch" })

    client.Query<Event> "select * from testseries"
    |> Async.RunSynchronously
    |> function
       | Choice1Of2 result -> printfn "%A" result
       | Choice2Of2 error -> printfn "%A" error

    Console.ReadLine () |> ignore
    0
