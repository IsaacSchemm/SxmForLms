namespace SxmForLms

open System
open System.IO
open System.Net.Sockets
open System.Text
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks

open Microsoft.Extensions.Hosting

module LyrionCLI =
    let ip = "192.168.4.36"
    let port = 9090

    let encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier = false)

    let recieved = new Event<string list>()

    let reader = recieved.Publish
    let channel = Channel.CreateUnbounded<string>()

    exception NotConnectedException

    let sendAsync command =
        command
        |> Seq.map Uri.EscapeDataString
        |> String.concat " "
        |> channel.Writer.WriteAsync

    type Service() =
        inherit BackgroundService()

        override _.ExecuteAsync(cancellationToken) = task {
            while not cancellationToken.IsCancellationRequested do
                printfn $"Connecting to port {port}"

                let client = new TcpClient()

                do! client.ConnectAsync(ip, port, cancellationToken)
                use stream = client.GetStream()

                let readTask = task {
                    try
                        use sr = new StreamReader(stream, Encoding.UTF8)

                        printfn $"Connected to port {port}"

                        do! sendAsync ["subscribe"; "client,playlist,power,unknownir"]

                        let mutable finished = false
                        while client.Connected && not finished do
                            let! line = sr.ReadLineAsync(cancellationToken)
                            if isNull line then
                                client.Close()
                                finished <- true
                            else
                                let command =
                                    line
                                    |> Utility.split ' '
                                    |> Seq.map Uri.UnescapeDataString
                                    |> Seq.toList
                                recieved.Trigger(command)
                    with
                        | :? IOException when not client.Connected -> ()
                        | :? OperationCanceledException -> ()
                }

                let writeToken, cancelWrite =
                    let cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                    cts.Token, fun () -> cts.Cancel()

                ignore (task {
                    try
                        use sw = new StreamWriter(stream, encoding, AutoFlush = true)

                        while not writeToken.IsCancellationRequested do
                            let! string = channel.Reader.ReadAsync(writeToken)
                            let sb = new StringBuilder(string)
                            do! sw.WriteLineAsync(sb, writeToken)
                    with
                        | :? IOException when not client.Connected -> ()
                        | :? OperationCanceledException -> ()
                })

                do! readTask

                printfn $"Disconnecting from port {port}"

                client.Close()

                printfn $"Disconnected from port {port}"
                
                cancelWrite ()

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

        let period = TimeSpan.FromSeconds(5)

        let! completed = Task.WhenAny [
            Task.Delay(period)
            tcs.Task
        ]

        if completed <> tcs.Task then
            raise (NoMatchingResponseException (command, period))

        return! tcs.Task
    }

    type Player = Player of string

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
            | ["player"; "id"; Int32 i; id] when i = index -> Some (Player id)
            | _ -> None)

        let getPowerAsync (Player id) = listenForAsync [id; "power"; "?"] (fun command ->
            match command with
            | [x; "power"; "0"] when x = id -> Some false
            | [x; "power"; "1"] when x = id -> Some true
            | _ -> None)

        let setPowerAsync (Player id) state = sendAsync [
            id
            "power"
            if state then "1" else "0"
        ]

        let togglePowerAsync (Player id) = sendAsync [
            id
            "power"
        ]

        let getVolumeAsync (Player id) = listenForAsync [id; "mixer"; "volume"; "?"] (fun command ->
            match command with
            | [x; "mixer"; "volume"; Decimal value] when x = id -> Some value
            | _ -> None)

        let setVolumeAsync (Player id) volume = sendAsync [
            id
            "mixer"
            "volume"
            volume
        ]

        let getMutingAsync (Player id) = listenForAsync [id; "mixer"; "muting"; "?"] (fun command ->
            match command with
            | [x; "mixer"; "muting"; "0"] when x = id -> Some false
            | [x; "mixer"; "muting"; "1"] when x = id -> Some true
            | _ -> None)

        let setMutingAsync (Player id) state = sendAsync [
            id
            "mixer"
            "muting"
            if state then "1" else "0"
        ]

        let toggleMutingAsync (Player id) = sendAsync [
            id
            "mixer"
            "muting"
        ]

        let getDisplayAsync (Player id) = listenForAsync [id; "display"; "?"; "?"] (fun command ->
            match command with
            | [x; "display"; line1; line2] when x = id -> Some (line1, line2)
            | _ -> None)

        let getDisplayNowAsync (Player id) = listenForAsync [id; "displaynow"; "?"; "?"] (fun command ->
            match command with
            | [x; "displaynow"; line1; line2] when x = id -> Some (line1, line2)
            | _ -> None)

        let setDisplayAsync (Player id) line1 line2 (duration: TimeSpan) = sendAsync [
            id
            "display"
            line1
            line2
            $"{duration.TotalSeconds}"
        ]

        let simulateButtonAsync (Player id) buttoncode = sendAsync [
            id
            "button"
            buttoncode
        ]

        let simulateIRAsync (Player id) (ircode: int) (time: decimal) = sendAsync [
            id
            "ir"
            ircode.ToString("x8")
            time.ToString()
        ]

    module Playlist =
        let playAsync (Player id) = sendAsync [
            id
            "play"
        ]

        let stopAsync (Player id) = sendAsync [
            id
            "stop"
        ]

        let setPauseAsync (Player id) state = sendAsync [
            id
            "pause"
            if state then "1" else "0"
        ]

        let setTimeAsync (Player id) time = sendAsync [
            id
            "time"
            sprintf "%d" time
        ]

        let togglePauseAsync (Player id) = sendAsync [
            id
            "pause"
        ]

        type Mode = Playing | Stopped | Paused

        let getModeAsync (Player id) = listenForAsync [id; "mode"; "?"] (fun command ->
            match command with
            | [x; "mode"; "play"] when x = id -> Some Playing
            | [x; "mode"; "stop"] when x = id -> Some Stopped
            | [x; "mode"; "pause"] when x = id -> Some Paused
            | _ -> None)

        let playItemAsync (Player id) item title = sendAsync [
            id
            "playlist"
            "play"
            item
            title
        ]

        let getPathAsync (Player id) = listenForAsync [id; "path"; "?"] (fun command ->
            match command with
            | [x; "path"; path] when x = id -> Some path
            | b -> None)
