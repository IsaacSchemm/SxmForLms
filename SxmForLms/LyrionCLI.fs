namespace SxmForLms

open System
open System.IO
open System.Net.Sockets
open System.Text
open System.Threading
open System.Threading.Tasks

open Microsoft.Extensions.Hosting

module LyrionCLI =
    let ip = "192.168.4.36"

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

        let waitFor = TimeSpan.FromSeconds(3)

        let! completed = Task.WhenAny [
            Task.Delay(waitFor)
            tcs.Task
        ]

        if completed <> tcs.Task then
            failwith $"Did not recieve expected response from LMS within {waitFor}"

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

        let setPowerAsync playerid = sendAsync [
            sprintf "%s" playerid
            "power"
        ]

        let togglePowerAsync playerid state = sendAsync [
            sprintf "%s" playerid
            "power"
            if state then "1" else "0"
        ]

        let getVolumeAsync playerid = listenForAsync [playerid; "mixer"; "volume"; "?"] (fun command ->
            match command with
            | [id; "mixer"; "volume"; Decimal value] when id = playerid -> Some value
            | _ -> None)

        let setVolumeAsync playerid volume = sendAsync [
            sprintf "%s" playerid
            "mixer"
            "volume"
            volume
        ]

        let getMutingAsync playerid = listenForAsync [playerid; "mixer"; "muting"; "?"] (fun command ->
            match command with
            | [id; "mixer"; "muting"; "0"] when id = playerid -> Some false
            | [id; "mixer"; "muting"; "1"] when id = playerid -> Some true
            | _ -> None)

        let setMutingAsync playerid state = sendAsync [
            sprintf "%s" playerid
            "mixer"
            "muting"
            if state then "1" else "0"
        ]

        let toggleMutingAsync playerid = sendAsync [
            sprintf "%s" playerid
            "mixer"
            "muting"
        ]

        let getDisplayAsync playerid = listenForAsync [playerid; "display"; "?"; "?"] (fun command ->
            match command with
            | [id; "display"; line1; line2] when id = playerid -> Some (line1, line2)
            | _ -> None)

        let getDisplayNowAsync playerid = listenForAsync [playerid; "displaynow"; "?"; "?"] (fun command ->
            match command with
            | [id; "displaynow"; line1; line2] when id = playerid -> Some (line1, line2)
            | _ -> None)

        let setDisplayAsync playerid line1 line2 (duration: TimeSpan) = sendAsync [
            sprintf "%s" playerid
            "display"
            line1
            line2
            $"{duration.TotalSeconds}"
        ]
