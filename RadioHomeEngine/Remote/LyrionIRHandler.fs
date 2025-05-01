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
        let mutable buttonsPressed = Map.empty

        let doOnceAsync ircode action = task {
            let now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

            let lastPressedAt =
                buttonsPressed
                |> Map.tryFind ircode
                |> Option.defaultValue 0L

            let wasPressed = lastPressedAt > now - 250L

            buttonsPressed <-
                buttonsPressed
                |> Map.add ircode now

            if not wasPressed then
                do! action ()
        }

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
            let! address = Network.getAddressAsync CancellationToken.None
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

        let playAllTracksAsync (startAtTrack: int) = task {
            do! Players.simulateButtonAsync player "stop"

            do! Players.setDisplayAsync player "Please wait" "Searching for tracks..." (TimeSpan.FromSeconds(999))

            let disc = Icedax.getInfo ()

            do! clearAsync ()

            do! Playlist.clearAsync player

            let! address = Network.getAddressAsync CancellationToken.None
            for track in disc.tracks |> Seq.skipWhile (fun t -> t.number < startAtTrack) do
                let title =
                    match track.title with
                    | "" -> $"Track {track.number}"
                    | x -> x
                do! Playlist.addItemAsync player $"http://{address}:{Config.port}/CD/PlayTrack?track={track.number}" title

            do! Playlist.playAsync player
        }

        let mutable holdTime = ref DateTime.UtcNow

        let customActionAsync action = task {
            let! powerState = LyrionKnownPlayers.PowerStates.getStateAsync player
            if powerState then
                match action with
                | StreamInfo ->
                    let! channelId = getCurrentSiriusXMChannelId ()
                    match channelId with
                    | None -> ()
                    | Some id ->
                        do! Players.setDisplayAsync player "SiriusXM" "Please wait..." (TimeSpan.FromSeconds(5))

                        let! channels = SiriusXMClient.getChannelsAsync CancellationToken.None
                        let channel =
                            channels
                            |> Seq.where (fun c -> c.channelId = id)
                            |> Seq.tryHead

                        match channel with
                        | None ->
                            do! clearAsync ()
                        | Some c ->
                            let! playlist = SiriusXMClient.getPlaylistAsync c.channelGuid c.channelId CancellationToken.None
                            let song =
                                playlist.cuts
                                |> Seq.sortByDescending (fun cut -> cut.startTime)
                                |> Seq.tryHead

                            match song with
                            | None ->
                                do! clearAsync ()
                            | Some c ->
                                let artist = String.concat " / " c.artists
                                do! Players.setDisplayAsync player artist c.title (TimeSpan.FromSeconds(5))

                | Eject ->
                    use proc = Process.Start("eject", $"-T {Icedax.device}")
                    do! proc.WaitForExitAsync()

                | PlayAllTracks ->
                    do! playAllTracksAsync 0

                | Forecast ->
                    do! Players.setDisplayAsync player "Forecast" "Please wait..." (TimeSpan.FromSeconds(5))

                    let! forecasts = Weather.getForecastsAsync CancellationToken.None
                    let! alerts = Weather.getAlertsAsync CancellationToken.None

                    do! Speech.readAsync player [
                        for forecast in Seq.truncate 2 forecasts do
                            forecast
                        for alert in alerts do
                            alert.info
                    ]

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
                | CustomNumeric n ->
                    if behavior = LoadPresetSingle then
                        do! Players.simulateButtonAsync player $"playPreset_{n}"
                    else if powerState then
                        do! writePromptAsync $"> {n}"
        }

        let pressAsync pressAction = task {
            match pressAction with
            | Button button ->
                do! Players.simulateButtonAsync player button
            | Custom customAction ->
                do! customActionAsync customAction
        }

        let processIRAsync ircode time = task {
            holdTime.Value <- DateTime.UtcNow

            let mapping =
                Mappings
                |> Map.tryFind ircode
                |> Option.defaultValue NoAction

            let processPromptEntryAsync (prompt: string) = task {
                do! doOnceAsync ircode (fun () -> task {
                    match mapping with
                    | Number n ->
                        do! appendToPromptAsync n

                    | Press (Custom Backspace) when prompt = "> " ->
                        do! clearAsync ()

                    | Press (Custom Backspace) ->
                        do! writePromptAsync (prompt.Substring(0, prompt.Length - 1))

                    | Press (Button "knob_push") when behavior = SeekToSeconds ->
                        match prompt.Substring(2) with
                        | Int32 s ->
                            do! clearAsync ()
                            do! Playlist.setTimeAsync player s
                        | _ ->
                            do! writePromptAsync "> "

                    | Press (Button "knob_push") when behavior = SeekToMinutes ->
                        match prompt.Substring(2) with
                        | Int32 m ->
                            do! clearAsync ()
                            do! Playlist.setTimeAsync player (60 * m)
                        | _ ->
                            do! writePromptAsync "> "

                    | Press (Button "knob_push") when behavior = AudioCD ->
                        let num = prompt.Substring(2)

                        match num with
                        | "" ->
                            do! Players.setDisplayAsync player "Ejecting CD" "Please wait..." (TimeSpan.FromSeconds(10))
                            use proc = Process.Start("eject", $"-T {Icedax.device}")
                            do! proc.WaitForExitAsync()
                            do! clearAsync ()
                        | Int32 track ->
                            do! clearAsync ()
                            do! playAllTracksAsync track
                        | _ ->
                            do! writePromptAsync "> "

                    | Press (Button "knob_push") when behavior = SiriusXM ->
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
                })
            }

            let processNormalEntryAsync () = task {
                match mapping with
                | Hold (short, long) ->
                    do! doOnceAsync ircode (fun () -> task {
                        let start = DateTime.UtcNow

                        let mutable actionTriggered = false

                        while DateTime.UtcNow - holdTime.Value < TimeSpan.FromMilliseconds(250) do
                            do! Task.Delay(100)

                            if not actionTriggered then
                                if DateTime.UtcNow - start >= TimeSpan.FromSeconds(2) then
                                    do! clearAsync ()
                                    do! pressAsync long
                                    actionTriggered <- true

                        if not actionTriggered then
                            do! pressAsync short
                    })

                | Number n when behavior = Digit ->
                    do! Players.simulateIRAsync player Slim[$"{n}"] time

                | Number n ->
                    do! doOnceAsync ircode (fun () -> task {
                        do! customActionAsync (CustomNumeric n)
                    })

                | Simulate name ->
                    do! Players.simulateIRAsync player Slim[name] time

                | Press pressAction ->
                    do! doOnceAsync ircode (fun () -> task {
                        do! pressAsync pressAction
                    })

                | _ -> ()
            }

            match promptText with
            | Some prompt -> do! processPromptEntryAsync prompt
            | None -> do! processNormalEntryAsync ()
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
