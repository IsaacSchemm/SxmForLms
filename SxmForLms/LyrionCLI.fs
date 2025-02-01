namespace SxmForLms

open System
open System.IO
open System.Net.Sockets
open System.Text
open System.Threading.Tasks

open Microsoft.Extensions.Hosting

module LyrionCLI =
    let ip = "localhost"

    let mutable private current = None
    let mutable private readTask = Task.CompletedTask
    let mutable private writer = TextWriter.Null

    let encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier = false)

    let recieved = new Event<string list>()

    let startAsync () = task {
        match current with
        | Some _ -> ()
        | None ->
            let client = new TcpClient()
            current <- Some client

            do! client.ConnectAsync(ip, 9090)
            let stream = client.GetStream()

            readTask <- task {
                use sr = new StreamReader(stream, Encoding.UTF8)
                try
                    while current.Value.Connected do
                        let! line = sr.ReadLineAsync()
                        printfn "%s" line
                        let command =
                            line.Split(' ')
                            |> Seq.map Uri.UnescapeDataString
                            |> Seq.toList
                        recieved.Trigger(command)
                with :? IOException when not current.Value.Connected -> ()
            }

            let sw = new StreamWriter(stream, encoding, AutoFlush = true)

            writer <- sw
    }

    let sendAsync (command: string seq) =
        command
        |> Seq.map Uri.EscapeDataString
        |> String.concat " "
        |> writer.WriteLineAsync

    let stopAsync () = task {
        match current with
        | Some client ->
            client.Close()
            writer.Dispose()

            do! readTask

            current <- None
            readTask <- Task.CompletedTask
            writer <- TextWriter.Null
        | None -> ()
    }

    type Service() =
        inherit BackgroundService()

        override _.ExecuteAsync(cancellationToken) = task {
            while not cancellationToken.IsCancellationRequested do
                do! startAsync ()

                try
                    do! Task.Delay(TimeSpan.FromMinutes(5), cancellationToken)
                with :? TaskCanceledException -> ()

            do! stopAsync ()
        }

    let listenForAsync command chooser = task {
        let tcs = new TaskCompletionSource<'T>()

        use _ =
            recieved.Publish
            |> Observable.choose chooser
            |> Observable.subscribe tcs.SetResult

        do! sendAsync command

        return! tcs.Task
    }

    module Players =
        let countAsync () = listenForAsync ["player"; "count"; "?"] (fun command ->
            match command with
            | ["player"; "count"; Int32 count] -> Some count
            | _ -> None)

        let getIdAsync index = listenForAsync ["player"; "id"; sprintf "%d" index; "?"] (fun command ->
            match command with
            | ["player"; "id"; Int32 i; id] when i = index -> Some id
            | _ -> None)

        let getPowerAsync playerid = listenForAsync [playerid; "power"; "?"] (fun command ->
            match command with
            | [id; "power"; "0"] when id = playerid -> Some false
            | [id; "power"; "1"] when id = playerid -> Some true
            | _ -> None)

        let setPowerAsync playerid =
            sendAsync [playerid; "power"]

        let togglePowerAsync playerid state =
            sendAsync [playerid; "power"; (if state then "1" else "0")]

        let getVolumeAsync playerid = listenForAsync [playerid; "mixer"; "volume"; "?"] (fun command ->
            match command with
            | [id; "mixer"; "volume"; Decimal value] when id = playerid -> Some value
            | _ -> None)

        let setVolumeAsync playerid volume =
            sendAsync [playerid; "mixer"; "volume"; volume]

        let getMutingAsync playerid = listenForAsync [playerid; "mixer"; "muting"; "?"] (fun command ->
            match command with
            | [id; "mixer"; "muting"; "0"] when id = playerid -> Some false
            | [id; "mixer"; "muting"; "1"] when id = playerid -> Some true
            | _ -> None)

        let setMutingAsync playerid state =
            sendAsync [playerid; "mixer"; "muting"; (if state then "1" else "0")]

        let toggleMutingAsync playerid =
            sendAsync [playerid; "mixer"; "muting"]

        type Brightness =
        | PowerOn
        | PowerOff
        | Idle
        | Brightness of int

        type ShowMessage = {
            line1: string option
            line2: string option
            duration: TimeSpan option
            brightness: Brightness option
            huge: bool
            centered: bool
        }

        let showAsync (playerid: string) message =
            sendAsync [
                playerid

                "show"

                match message.line1 with Some x -> $"line1:{x}" | None -> ()
                match message.line2 with Some x -> $"line2:{x}" | None -> ()
                match message.duration with Some x -> $"duration:{x.TotalSeconds}" | None -> ()

                match message.brightness with
                | Some PowerOn -> "brightness:powerOn"
                | Some PowerOff -> "brightness:powerOff"
                | Some Idle -> "brightness:idle"
                | Some (Brightness x) -> $"brightness:{x}"
                | None -> ()

                if message.huge then "font:huge"
                if message.centered then "centered:1"
            ]

        let getDisplayAsync playerid = listenForAsync [playerid; "display"; "?"; "?"] (fun command ->
            match command with
            | [id; "display"; line1; line2] when id = playerid -> Some (line1, line2)
            | id :: "show" :: _ when id = playerid -> raise (new NotImplementedException())
            | _ -> None)
