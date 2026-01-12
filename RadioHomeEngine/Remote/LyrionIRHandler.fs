namespace RadioHomeEngine

open System
open System.Diagnostics
open System.Globalization
open System.IO
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks

open Microsoft.Extensions.Hosting

open LyrionCLI
open LyrionIR

module LyrionIRHandler =
    type DigitBehavior =
    | Nothing
    | Passthrough
    | PlayPreset
    | SeekToSeconds
    | SeekToMinutes
    | PlayForecast
    | PlayCD
    | RipCD
    | EjectCD
    | SiriusXM
    | ViewSiriusXMInfo

    let enabledBehaviors = [
        Nothing
        Passthrough
        PlayPreset
        SeekToSeconds
        SeekToMinutes
        PlayForecast
        PlayCD
        RipCD
        EjectCD
        SiriusXM
        ViewSiriusXMInfo
        Nothing
    ]

    let getBehaviorTitle behavior =
        match behavior with
        | Nothing -> "N/A"
        | Passthrough -> "Passthrough"
        | PlayPreset -> "Play preset (1-9)"
        | SeekToSeconds -> "Seek to (seconds)"
        | SeekToMinutes -> "Seek to (minutes)"
        | PlayForecast -> "Play weather forecast"
        | PlayCD -> "Play CD"
        | RipCD -> "Rip CD"
        | EjectCD -> "Eject CD"
        | SiriusXM -> "Play SiriusXM channel"
        | ViewSiriusXMInfo -> "View SiriusXM program name"

    let (|IRCode|_|) (str: string) =
        match Int32.TryParse(str, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) with
        | true, value -> Some value
        | false, _ -> None

    type Handler(player: Player) =
        let getCurrentSiriusXMChannelId () = task {
            let! path = Playlist.getPathAsync player

            match Uri.TryCreate(path, UriKind.Absolute) with
            | true, uri ->
                let proxyPathPattern = new Regex("""^/Proxy/playlist-(.+)\.m3u8$""")
                let m = proxyPathPattern.Match(uri.AbsolutePath)

                if m.Success then
                    return Some m.Groups[1].Value
                else
                    return None
            | false, _ ->
                return None
        }

        let mutable channelChanging = false

        let playSiriusXMChannelAsync channelNumber name = task {
            let! address = Network.getAddressAsync ()
            let url = $"http://{address}:{Config.port}/SXM/PlayChannel?num={channelNumber}"
            let name = $"[{channelNumber}] {name}"
            do! Playlist.playItemAsync player url name
            channelChanging <- true
        }

        let mutable behavior = Seq.head enabledBehaviors

        let mutable promptText = None
        let mutable promptMonitor = Task.CompletedTask

        let writePromptAsync text = task {
            promptText <- Some text

            let header = getBehaviorTitle behavior
            do! Players.setDisplayAsync player header text (TimeSpan.FromSeconds(10))

            if promptMonitor.IsCompleted then
                promptMonitor <- task {
                    try
                        printf "Monitoring remote screen..."
                        let mutable finished = false
                        while not finished do
                            printf "."
                            do! Task.Delay(200)
                            let! (current, _) = Players.getDisplayNowAsync player
                            if current <> header then
                                printfn " screen reset."
                                finished <- true
                    with ex ->
                        Console.Error.WriteLine(ex)

                    promptText <- None
                }
        }

        let appendToPromptAsync text =
            match promptText with
            | Some prefix -> writePromptAsync $"{prefix}{text}"
            | None -> writePromptAsync $"> {text}"

        let clearAsync () = task {
            promptText <- None
            do! Players.setDisplayAsync player " " " " (TimeSpan.FromMilliseconds(1))
        }

        let mutable held = 0
        let mutable firstPressed = 0L
        let mutable lastPressed = 0L
        let mutable lastIRTime = 0M

        let simulateIRAsync name = task {
            do! Players.simulateIRAsync player Slim[name] lastIRTime
        }

        let performCustomActionAsync customAction = task {
            match customAction with
            | ChannelUp | ChannelDown when channelChanging ->
                ()

            | ChannelUp ->
                if not channelChanging then
                    let! id = getCurrentSiriusXMChannelId ()

                    if Option.isSome id then
                        let! channels = SiriusXMClient.getChannelsAsync CancellationToken.None

                        for (a, b) in Seq.pairwise channels do
                            if Some a.channelId = id then
                                do! playSiriusXMChannelAsync b.channelNumber b.name

            | ChannelDown ->
                if not channelChanging then
                    let! id = getCurrentSiriusXMChannelId ()

                    if Option.isSome id then
                        let! channels = SiriusXMClient.getChannelsAsync CancellationToken.None

                        for (a, b) in Seq.pairwise channels do
                            if Some b.channelId = id then
                                do! playSiriusXMChannelAsync a.channelNumber a.name

            | Input ->
                let! (header, _) = Players.getDisplayNowAsync player
                if header = "IR Remote Numeric Entry" then
                    behavior <-
                        Seq.pairwise enabledBehaviors
                        |> Seq.where (fun (a, _) -> a = behavior)
                        |> Seq.map (fun (_, b) -> b)
                        |> Seq.head

                do! Players.setDisplayAsync player "IR Remote Numeric Entry" (getBehaviorTitle behavior) (TimeSpan.FromSeconds(3))

            | Backspace -> ()
        }

        let pressAsync press = task {
            match press with
            | Button button ->
                do! Players.simulateButtonAsync player button

            | Custom action ->
                let! powerState = LyrionKnownPlayers.PowerStates.getStateAsync player
                if powerState then
                    do! performCustomActionAsync action

            | Atomic action ->
                let! powerState = LyrionKnownPlayers.PowerStates.getStateAsync player
                if powerState then
                    do! AtomicActions.performActionAsync player action

            | IRPress name ->
                do! simulateIRAsync name

            | Number n when behavior = PlayPreset ->
                do! Players.simulateButtonAsync player $"playPreset_{n}"

            | Number n ->
                let! powerState = LyrionKnownPlayers.PowerStates.getStateAsync player
                if powerState then
                    let prompt =
                        match behavior with
                        | Nothing -> "Press Input/Source to set up number buttons"
                        | SeekToSeconds | SeekToMinutes | SiriusXM -> $"> {n}"
                        | _ -> "Press OK to continue"
                    do! writePromptAsync prompt
        }

        let processPromptEntryAsync press = task {
            let prompt =
                promptText
                |> Option.defaultValue "> "

            match press, behavior with
            | Number n, SeekToSeconds
            | Number n, SeekToMinutes
            | Number n, PlayCD
            | Number n, SiriusXM ->
                do! appendToPromptAsync n

            | Custom Backspace, _ when prompt = "> " ->
                do! clearAsync ()

            | Custom Backspace, _ when prompt.StartsWith("> ") ->
                do! writePromptAsync (prompt.Substring(0, prompt.Length - 1))

            | Button "knob_push", SeekToSeconds ->
                match prompt.Substring(2) with
                | Int32 s ->
                    do! clearAsync ()
                    do! Playlist.setTimeAsync player SeekOrigin.Begin (decimal s)
                | _ ->
                    do! writePromptAsync "> "

            | Button "knob_push", SeekToMinutes ->
                match prompt.Substring(2) with
                | Int32 m ->
                    do! clearAsync ()
                    do! Playlist.setTimeAsync player SeekOrigin.Begin (60m * decimal m)
                | _ ->
                    do! writePromptAsync "> "

            | Button "knob_push", PlayForecast ->
                do! AtomicActions.performActionAsync player AtomicAction.Forecast

            | Button "knob_push", PlayCD ->
                do! clearAsync ()
                do! AtomicActions.performActionAsync player AtomicAction.PlayAllDiscs

            | Button "knob_push", RipCD ->
                do! Players.setDisplayAsync player "Ripping CD" "Ripping process started." (TimeSpan.FromSeconds(10))
                do! AtomicActions.performActionAsync player AtomicAction.RipAllDiscs

            | Button "knob_push", EjectCD ->
                do! clearAsync ()
                do! AtomicActions.performActionAsync player AtomicAction.EjectAllDiscs

            | Button "knob_push", SiriusXM ->
                let num = prompt.Substring(2)

                let! channels = SiriusXMClient.getChannelsAsync CancellationToken.None
                let channel =
                    channels
                    |> Seq.where (fun c -> c.channelNumber = num)
                    |> Seq.tryHead

                match channel with
                | Some c ->
                    do! clearAsync ()
                    do! playSiriusXMChannelAsync c.channelNumber c.name
                | None ->
                    do! writePromptAsync "> "

            | Button "knob_push", ViewSiriusXMInfo ->
                do! clearAsync ()
                do! AtomicActions.performActionAsync player AtomicAction.SiriusXMNowPlaying

            | _ ->
                do! clearAsync ()
        }

        let processPressAsync press = task {
            if Option.isSome promptText then
                do! processPromptEntryAsync press
            else
                do! pressAsync press
        }

        let processIRAsync ircode time = task {
            let mapping =
                Mappings
                |> Map.tryFind ircode
                |> Option.defaultValue NoAction

            let now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

            let lastPressedAt =
                if ircode = held
                then lastPressed
                else 0L

            let wasPressed = lastPressedAt > now - 250L

            if not wasPressed then
                held <- ircode
                firstPressed <- now

            lastPressed <- now
            lastIRTime <- time

            let pressedRecently () =
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastPressed <= 150

            match mapping with
            | IR name ->
                do! simulateIRAsync name

            | Series [Number n] when behavior = Passthrough ->
                do! simulateIRAsync $"{n}"

            | Series presses ->
                if firstPressed = lastPressed then
                    let start = firstPressed

                    let buffer = ref presses

                    let walker = task {
                        while firstPressed = start && pressedRecently () && buffer.Value.Length > 1 do
                            do! Task.Delay(2000)
                            buffer.Value <- List.tail buffer.Value
                    }

                    while firstPressed = start && pressedRecently () && buffer.Value.Length > 1 do
                        do! Task.Delay(50)

                    let press = (List.head buffer.Value)

                    do! processPressAsync press

                    do! walker

            | _ -> ()
        }

        let processCommandAsync command = task {
            try
                match command with
                | [x; "playlist"; "newsong"; _; _] when Player x = player ->
                    channelChanging <- false
                | [x; "unknownir"; IRCode ircode; Decimal time] when Player x = player ->
                    do! processIRAsync ircode time
                | _ -> ()
            with ex -> Console.Error.WriteLine(ex)
        }

        let subscriber = reader |> Observable.subscribe (fun command ->
            ignore (processCommandAsync command))

        member _.Player = player

        member _.ProcessPressAsync(press) = processPressAsync press

        interface IDisposable with
            member _.Dispose() = subscriber.Dispose()

    let mutable private handlers: Handler list = []

    let ProcessPressAsync(player: Player, press: Press) = task {
        for handler in handlers do
            if handler.Player = player then
                do! handler.ProcessPressAsync(press)
    }

    type Service() =
        inherit BackgroundService()

        override _.ExecuteAsync(cancellationToken) = task {
            LyrionKnownPlayers.WhenAdded.attachHandler (fun player ->
                printfn "Creating IR handler for %A" player
                handlers <- new Handler(player) :: handlers
            )

            while not cancellationToken.IsCancellationRequested do
                do! Task.Delay(-1, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)

            for x in handlers do
                (x :> IDisposable).Dispose()
        }
