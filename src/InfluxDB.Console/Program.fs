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

    client.Series ("testseries", { User = "andrew"; Action = "got lunch" })

    Console.ReadLine () |> ignore
    0
