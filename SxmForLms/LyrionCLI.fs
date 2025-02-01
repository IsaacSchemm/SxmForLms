namespace SxmForLms

open System
open System.IO
open System.Net.Sockets
open System.Text
open System.Threading.Tasks

open Microsoft.Extensions.Hosting

module LyrionCLI =
    let ip = "localhost"
    let port = 9090

    let encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier = false)

    let recieved = new Event<string list>()

    let reader = recieved.Publish
    let mutable writer = TextWriter.Null

    let sendAsync (command: string seq) =
        command
        |> Seq.map Uri.EscapeDataString
        |> String.concat " "
        |> writer.WriteLineAsync

    type Service() =
        inherit BackgroundService()

        override _.ExecuteAsync(cancellationToken) = task {
            while not cancellationToken.IsCancellationRequested do
                printfn $"Connecting to port {port}"

                let client = new TcpClient()

                try
                    do! client.ConnectAsync(ip, port, cancellationToken)
                    use stream = client.GetStream()
            
                    use sr = new StreamReader(stream, Encoding.UTF8)
                    use sw = new StreamWriter(stream, encoding, AutoFlush = true)

                    writer <- sw

                    printfn $"Connected to port {port}"

                    let mutable finished = false
                    while client.Connected && not finished do
                        let! line = sr.ReadLineAsync(cancellationToken)
                        if isNull line then
                            client.Close()
                            finished <- true
                        else
                            let command =
                                line.Split(' ')
                                |> Seq.map Uri.UnescapeDataString
                                |> Seq.toList
                            recieved.Trigger(command)
                with
                    | :? IOException when not client.Connected -> ()
                    | :? TaskCanceledException -> ()

                printfn $"Disconnecting from port {port}"

                writer <- TextWriter.Null

                client.Close()
                writer.Dispose()

                printfn $"Disconnected from port {port}"

                do! Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)
        }

    exception NoMatchingResponseException of command: string list * period: TimeSpan

    let listenForAsync command chooser = task {
        let tcs = new TaskCompletionSource<'T>()

        use _ =
            recieved.Publish
            |> Observable.choose chooser
            |> Observable.subscribe tcs.SetResult

        do! sendAsync command

        let period = TimeSpan.FromSeconds(3)

        let! completed = Task.WhenAny [
            Task.Delay(period)
            tcs.Task
        ]

        if completed <> tcs.Task then
            raise (NoMatchingResponseException (command, period))

        return! tcs.Task
    }

    module General =
        let exitAsync () = sendAsync ["exit"]
        let restartServer () = sendAsync ["restartserver"]

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

    module Playlist =
        let playAsync playerid = sendAsync [
            sprintf "%s" playerid
            "play"
        ]

        let stopAsync playerid = sendAsync [
            sprintf "%s" playerid
            "stop"
        ]

        let setPauseAsync playerid state = sendAsync [
            sprintf "%s" playerid
            "pause"
            if state then "1" else "0"
        ]

        let togglePauseAsync playerid = sendAsync [
            sprintf "%s" playerid
            "pause"
        ]

        type Mode = Playing | Stopped | Paused

        let getModeAsync playerid = listenForAsync [playerid; "mode"; "?"] (fun command ->
            match command with
            | [id; "mode"; "play"] when id = playerid -> Some Playing
            | [id; "mode"; "stop"] when id = playerid -> Some Stopped
            | [id; "mode"; "pause"] when id = playerid -> Some Paused
            | _ -> None)

        let playItemAsync playerid item title = sendAsync [
            sprintf "%s" playerid
            "play"
            item
            title
        ]

        let getPathAsync playerid = listenForAsync [playerid; "path"; "?"] (fun command ->
            match command with
            | [id; "path"; path] when id = playerid -> Some path
            | _ -> None)
