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
    | SeekTo
    | AudioCD
    | SiriusXM

    let enabledBehaviors = [
        Digit
        LoadPresetSingle
        SeekTo
        AudioCD
        SiriusXM
        Digit
    ]

    let getTitle behavior =
        match behavior with
        | Digit -> "Direct"
        | LoadPresetSingle -> "Preset (1-9)"
        | SeekTo -> "Seek to"
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

        let pressAsync pressAction = task {
            match pressAction with
            | Button button ->
                do! Players.simulateButtonAsync player button
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
        }

        let processIRAsync ircode time = task {
            holdTime.Value <- DateTime.UtcNow

            let! powerState = LyrionKnownPlayers.PowerStates.getStateAsync player

            let mappings =
                if powerState
                then MappingsOn
                else MappingsOff

            let mapping =
                mappings
                |> Map.tryFind ircode
                |> Option.defaultValue NoAction

            let processPromptEntryAsync (prompt: string) = task {
                do! doOnceAsync ircode (fun () -> task {
                    match mapping with
                    | Simulate str when str.Length = 1 && "0123456789".Contains(str) ->
                        do! appendToPromptAsync str

                    | PromptPress (Dot, _) when behavior = SeekTo ->
                        do! appendToPromptAsync ":"

                    | PromptPress (Backspace, _) when prompt = "> " ->
                        do! clearAsync ()

                    | PromptPress (Backspace, _) ->
                        do! writePromptAsync (prompt.Substring(0, prompt.Length - 1))

                    | Press (Button "knob_push") when behavior = SeekTo ->
                        let array =
                            prompt.Substring(2)
                            |> Utility.split ':'
                            |> Array.map Int32.TryParse

                        let time =
                            match array with
                            | [| (true, s) |] ->
                                Some s
                            | [| (true, m); (true, s) |] ->
                                Some (60 * m + s)
                            | [| (true, h); (true, m); (true, s) |] ->
                                Some (3600 * h + 60 * m + s)
                            | _ ->
                                None

                        match time with
                        | Some t ->
                            do! clearAsync ()
                            do! Playlist.setTimeAsync player t
                        | _ ->
                            do! writePromptAsync "> "

                    | Press (Button "knob_push") when behavior = AudioCD ->
                        let num = prompt.Substring(2)

                        match num with
                        | "" ->
                            do! clearAsync ()
                            do! playAllTracksAsync 0
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

                    | Press (Button "exit_left")
                    | Press Input ->
                        do! clearAsync ()

                    | _ -> ()
                })
            }

            let processNormalEntryAsync () = task {
                match mapping with
                | Hold actionList ->
                    do! doOnceAsync ircode (fun () -> task {
                        for a in actionList do
                            match a with
                            | Message m -> do! Players.setDisplayAsync player m m (TimeSpan.FromSeconds(5))
                            | _ -> ()

                        let start = DateTime.UtcNow

                        let mutable actionTriggered = false

                        while DateTime.UtcNow - holdTime.Value < TimeSpan.FromMilliseconds(250) do
                            do! Task.Delay(100)

                            if not actionTriggered then
                                if DateTime.UtcNow - start >= TimeSpan.FromSeconds(2) then
                                    do! clearAsync ()

                                    for a in actionList do
                                        match a with
                                        | OnHold pressAction -> do! pressAsync pressAction
                                        | _ -> ()

                                    actionTriggered <- true

                        if not actionTriggered then
                            for a in actionList do
                            match a with
                            | Message m -> do! clearAsync ()
                            | OnRelease pressAction -> do! pressAsync pressAction
                            | _ -> ()
                    })

                | Simulate str when str.Length = 1 && "0123456789".Contains(str) && behavior <> Digit ->
                    do! doOnceAsync ircode (fun () -> task {
                        if behavior = LoadPresetSingle then
                            do! Players.simulateButtonAsync player $"playPreset_{str}"
                        else
                            do! writePromptAsync $"> {str}"
                    })

                | Simulate name ->
                    do! Players.simulateIRAsync player Slim[name] time

                | Press pressAction
                | PromptPress (_, pressAction) ->
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
