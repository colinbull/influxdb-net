namespace InfluxDB

open System


[<AutoOpen>]
module Configuration =

    type InfluxDBConfiguration =
        { Client: InfluxDBClientConfiguration
          Server: InfluxDBServerConfiguration }

    and InfluxDBServerConfiguration =
        { Host: string
          Port: int
          Database: string
          Username: string
          Password: string }

    and InfluxDBClientConfiguration =
        { MaxBufferSize: int
          MaxBufferTime: int }


[<AutoOpen>]
module Series =

    type InfluxSeries () =
        member val Name = String.Empty with get, set
        member val Columns = Array.empty<string> with get, set
        member val Points = Array.empty<obj array> with get, set


[<AutoOpen>]
module internal Buffering =

    open FSharpx

    type Buffer = Map<(string * string), obj list>
    
    let append buffer series e =
        buffer |> Lens.update 
            (fun x -> match x with | Some es -> Some (es @ [e]) | _ -> Some [e])
            (Lens.forMap (series, e.GetType().FullName))

    let makeBuffer () =
        Map.empty<(string * string), obj list>

    let size (buffer: Buffer) =
        buffer |> Map.fold (fun count _ events -> count + events.Length) 0


[<AutoOpen>]
module internal Serialization =

    open Microsoft.FSharp.Reflection
    open Newtonsoft.Json
    open Newtonsoft.Json.Serialization

    let private settings =
        JsonSerializerSettings (
            ContractResolver = CamelCasePropertyNamesContractResolver (),
            Formatting = Formatting.Indented)

    let map (buffer: Buffer) =
        buffer 
        |> Map.fold (fun series key value ->
            let columns, points =
                FSharpType.GetRecordFields (value.[0].GetType ())
                |> (fun fields ->
                    fields 
                    |> Array.map (fun i -> i.Name),
                    value 
                    |> Seq.map (fun e -> fields |> Array.map (fun i -> box (i.GetValue e))) 
                    |> Seq.toArray)
                     
            series @ [InfluxSeries (Name = fst key, Columns = columns, Points = points)]) List.empty
        |> List.toArray

    let serialize (series: InfluxSeries array) =
        JsonConvert.SerializeObject (series, settings)


[<AutoOpen>]
module internal Transmission =   

    open System.Net
    open System.Net.Http
    open System.Net.Http.Headers
    open System.Text
    open System.Web

    let flush (config: InfluxDBServerConfiguration) buffer : Buffer =
        let query = HttpUtility.ParseQueryString ("")
        query.["u"] <- config.Username
        query.["p"] <- config.Password

        let uri = UriBuilder ()
        uri.Host <- config.Host
        uri.Port <- config.Port
        uri.Path <- sprintf "db/%s/series" config.Database
        uri.Query <- query.ToString ()

        use content = new StringContent ((map >> serialize) buffer, Encoding.UTF8, "application/json")
        use http = new HttpClient ()

        let result =
            http.PostAsync (uri.Uri, content)
            |> Async.AwaitTask
            |> Async.RunSynchronously

        makeBuffer ()


[<AutoOpen>]
module Client =

    open FSharp.Control

    type private InfluxDBClientProtocol =
        | Send of series: string * event: obj

    type InfluxDBClient (config: InfluxDBConfiguration) =
        let flush = flush config.Server

        let agent = Agent.Start (fun agent ->
            let rec loop buffer =
                async {
                    let! msg = agent.TryReceive (config.Client.MaxBufferTime)

                    let buffer, doFlush =
                        match msg with
                        | (Some (Send (series, event))) ->
                            append buffer series event, size buffer >= (config.Client.MaxBufferSize - 1)
                        | _ ->
                            buffer, size buffer > 0

                    let buffer =
                        match doFlush with
                        | true -> flush buffer
                        | _ -> buffer

                    return! loop buffer }
                        
            loop (makeBuffer ()))

        member x.Series (series, data) =
            agent.Post (Send (series, data))
