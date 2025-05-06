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
    | Digit
    | LoadPresetSingle
    | SeekToSeconds
    | SeekToMinutes
    | AudioCD
    | SiriusXM

    let enabledBehaviors = [
        Digit
        LoadPresetSingle
        SeekToSeconds
        SeekToMinutes
        AudioCD
        SiriusXM
        Digit
    ]

    let getTitle behavior =
        match behavior with
        | Digit -> "Direct"
        | LoadPresetSingle -> "Preset (1-9)"
        | SeekToSeconds -> "Seek to (seconds)"
        | SeekToMinutes -> "Seek to (minutes)"
        | AudioCD -> "CD track"
        | SiriusXM -> "SiriusXM channel"

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
            let url = $"http://{address}:{Config.port}/Radio/PlayChannel?num={channelNumber}"
            let name = $"[{channelNumber}] {name}"
            do! Playlist.playItemAsync player url name
            channelChanging <- true
        }

        let mutable behavior = Seq.head (seq {
            if File.Exists("behavior.txt") then
                let str = File.ReadAllText("behavior.txt")
                for possibility in enabledBehaviors do
                    if str = $"{possibility}" then
                        possibility

            Seq.head enabledBehaviors
        })

        let mutable promptText = None
        let mutable promptMonitor = Task.CompletedTask

        let writePromptAsync text = task {
            promptText <- Some text

            let header = getTitle behavior
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
                if header = "Input Mode" then
                    behavior <-
                        Seq.pairwise enabledBehaviors
                        |> Seq.where (fun (a, _) -> a = behavior)
                        |> Seq.map (fun (_, b) -> b)
                        |> Seq.head

                do! Players.setDisplayAsync player "Input Mode" (getTitle behavior) (TimeSpan.FromSeconds(3))

                File.WriteAllText("behavior.txt", $"{behavior}")

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

            | Number n when behavior = LoadPresetSingle ->
                do! Players.simulateButtonAsync player $"playPreset_{n}"

            | Number n ->
                let! powerState = LyrionKnownPlayers.PowerStates.getStateAsync player
                if powerState then
                    do! writePromptAsync $"> {n}"
        }

        let processPromptEntryAsync press = task {
            let prompt =
                promptText
                |> Option.defaultValue "> "

            match press with
            | Number n ->
                do! appendToPromptAsync n

            | Custom Backspace when prompt = "> " ->
                do! clearAsync ()

            | Custom Backspace ->
                do! writePromptAsync (prompt.Substring(0, prompt.Length - 1))

            | Button "knob_push" when behavior = SeekToSeconds ->
                match prompt.Substring(2) with
                | Int32 s ->
                    do! clearAsync ()
                    do! Playlist.setTimeAsync player SeekOrigin.Begin (decimal s)
                | _ ->
                    do! writePromptAsync "> "

            | Button "knob_push" when behavior = SeekToMinutes ->
                match prompt.Substring(2) with
                | Int32 m ->
                    do! clearAsync ()
                    do! Playlist.setTimeAsync player SeekOrigin.Begin (60m * decimal m)
                | _ ->
                    do! writePromptAsync "> "

            | Button "knob_push" when behavior = AudioCD ->
                let num = prompt.Substring(2)

                match num with
                | "" ->
                    do! Players.setDisplayAsync player "Ejecting CD" "Please wait..." (TimeSpan.FromSeconds(10))
                    use proc = Process.Start("eject", $"-T {Icedax.device}")
                    do! proc.WaitForExitAsync()
                    do! clearAsync ()
                | Int32 track ->
                    do! clearAsync ()
                    do! AtomicActions.playAllTracksAsync player track
                | _ ->
                    do! writePromptAsync "> "

            | Button "knob_push" when behavior = SiriusXM ->
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

            | _ ->
                do! clearAsync ()
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

            match mapping with
            | IR name ->
                do! simulateIRAsync name

            | Series [Number n] when behavior = Digit ->
                do! simulateIRAsync $"{n}"

            | Series presses ->
                if firstPressed = lastPressed then
                    let start = firstPressed

                    let buffer = ref presses

                    let isFinished () =
                        firstPressed <> start
                        || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastPressed > 150
                        || buffer.Value.Length = 1

                    let walker = task {
                        while not (isFinished()) do
                            do! Task.Delay(2000)
                            buffer.Value <- List.tail buffer.Value
                    }

                    while not (isFinished()) do
                        do! Task.Delay(50)

                    let press = (List.head buffer.Value)

                    if Option.isSome promptText then
                        do! processPromptEntryAsync press
                    else
                        do! pressAsync press

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

        interface IDisposable with
            member _.Dispose() = subscriber.Dispose()

    type Service() =
        inherit BackgroundService()

        let mutable handlers: IDisposable list = []

        override _.ExecuteAsync(cancellationToken) = task {
            LyrionKnownPlayers.WhenAdded.attachHandler (fun player ->
                printfn "Creating IR handler for %A" player
                handlers <- new Handler(player) :: handlers
            )

            while not cancellationToken.IsCancellationRequested do
                do! Task.Delay(-1, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)

            for x in handlers do
                x.Dispose()
        }
