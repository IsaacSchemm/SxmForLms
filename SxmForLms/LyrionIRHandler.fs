namespace SxmForLms

open System
open System.Globalization
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
    | LoadPresetMulti
    | SeekTo
    | SiriusXM
    | Calculator

    let getTitle behavior =
        match behavior with
        | Digit -> "Direct digit entry"
        | LoadPresetSingle -> "Load preset (single-digit)"
        | LoadPresetMulti -> "Load preset (multi-digit)"
        | SeekTo -> "Seek (ss/mm:ss/hh:mm:ss)"
        | SiriusXM -> "Play SiriusXM channel"
        | Calculator -> "Calculator"

    let (|IRCode|_|) (str: string) =
        match Int32.TryParse(str, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) with
        | true, value -> Some value
        | false, _ -> None

    module SXM =
        let getChannelIdAsync player = task {
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

        let getSongsAsync channelId = task {
            let! channels = SiriusXMClient.getChannelsAsync CancellationToken.None
            let channel =
                channels
                |> Seq.where (fun c -> c.channelId = channelId)
                |> Seq.tryHead
            match channel with
            | Some c ->
                let! playlist = SiriusXMClient.getPlaylistAsync c.channelGuid c.channelId CancellationToken.None
                return playlist.cuts
            | None ->
                return []
        }

    let requestPowerState player =
        ignore (Players.getPowerAsync player)

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

        let showStreamInfo () = task {
            let! channelId = SXM.getChannelIdAsync player
            match channelId with
            | None -> ()
            | Some id ->
                let! songs = SXM.getSongsAsync id
                let song =
                    songs
                    |> Seq.sortByDescending (fun cut -> cut.startTime)
                    |> Seq.tryHead
                match song with
                | None -> ()
                | Some c ->
                    let artist = String.concat " / " c.artists
                    do! Players.setDisplayAsync player artist c.title (TimeSpan.FromSeconds(10))
        }

        let mutable channelChanging = false

        let playSiriusXMChannelAsync channelNumber name = task {
            let! address = Network.getAddressAsync CancellationToken.None
            let url = $"http://{address}:{Config.port}/Radio/PlayChannel?num={channelNumber}"
            let name = $"[{channelNumber}] {name}"
            do! Playlist.playItemAsync player url name
            channelChanging <- true
        }

        let mutable behavior = Digit
        let mutable promptText = None
        let mutable promptMonitor = Task.CompletedTask

        let writePromptAsync text = task {
            promptText <- Some text

            let header = getTitle behavior
            do! Players.setDisplayAsync player header text (TimeSpan.FromSeconds(10))

            if promptMonitor.IsCompleted then promptMonitor <- task {
                try
                    printf "Monitoring remote screen..."
                    let mutable finished = false
                    while not finished do
                        printf "."
                        do! Task.Delay(200)
                        let! (current, _) = Players.getDisplayNowAsync player
                        if current <> header then
                            printfn " screen reset."
                            promptText <- None
                            finished <- true
                with ex -> Console.Error.WriteLine(ex)
            }
        }

        let appendToPromptAsync text =
            match promptText with
            | Some prefix -> writePromptAsync $"{prefix}{text}"
            | None -> writePromptAsync $"> {text}"

        let clearAsync () = task {
            do! Players.setDisplayAsync player " " " " (TimeSpan.FromMilliseconds(1))
        }

        let mutable powerState = false

        let mutable holdTime = ref DateTime.UtcNow

        let pressAsync pressAction = task {
            match pressAction with
            | Button button ->
                do! Players.simulateButtonAsync player button
            | StreamInfo ->
                do! showStreamInfo ()
            | ChannelUp | ChannelDown when channelChanging ->
                ()
            | ChannelUp ->
                if not channelChanging then
                    let! id = SXM.getChannelIdAsync player

                    if Option.isSome id then
                        let! channels = SiriusXMClient.getChannelsAsync CancellationToken.None

                        for (a, b) in Seq.pairwise channels do
                            if Some a.channelId = id then
                                do! playSiriusXMChannelAsync b.channelNumber b.name
            | ChannelDown ->
                if not channelChanging then
                    let! id = SXM.getChannelIdAsync player

                    if Option.isSome id then
                        let! channels = SiriusXMClient.getChannelsAsync CancellationToken.None

                        for (a, b) in Seq.pairwise channels do
                            if Some b.channelId = id then
                                do! playSiriusXMChannelAsync a.channelNumber a.name
            | Input ->
                let all = seq {
                    Digit
                    LoadPresetSingle
                    LoadPresetMulti
                    SeekTo
                    SiriusXM
                    Calculator
                    Digit
                }

                let! (header, _) = Players.getDisplayNowAsync player
                if header = "Input Mode" then
                    behavior <-
                        Seq.pairwise all
                        |> Seq.where (fun (a, _) -> a = behavior)
                        |> Seq.map (fun (_, b) -> b)
                        |> Seq.head

                do! Players.setDisplayAsync player "Input Mode" (getTitle behavior) (TimeSpan.FromSeconds(3))
        }

        let processPromptEntryAsync ircode (prompt: string) = task {
            let mappings =
                if powerState
                then MappingsOn
                else MappingsOff

            let mapping =
                mappings
                |> Map.tryFind ircode
                |> Option.defaultValue NoAction

            do! doOnceAsync ircode (fun () -> task {
                match mapping with
                | Simulate str when str.Length = 1 && "0123456789".Contains(str) ->
                    do! appendToPromptAsync str

                | Dot when behavior = SeekTo ->
                    do! appendToPromptAsync ":"

                | Dot when behavior = Calculator ->
                    do! appendToPromptAsync "."

                | Simulate "arrow_up" when behavior = Calculator ->
                    do! appendToPromptAsync "("

                | Simulate "arrow_down" when behavior = Calculator ->
                    do! appendToPromptAsync ")"

                | Simulate "repeat" when behavior = Calculator ->
                    do! appendToPromptAsync "^"

                | Simulate "volup" when behavior = Calculator ->
                    do! appendToPromptAsync "+"

                | Simulate "voldown" when behavior = Calculator ->
                    do! appendToPromptAsync "-"

                | Press ChannelUp when behavior = Calculator ->
                    do! appendToPromptAsync "*"

                | Press ChannelDown when behavior = Calculator ->
                    do! appendToPromptAsync "/"

                | Simulate "rew" when behavior = Calculator ->
                    do! appendToPromptAsync "<<"

                | Simulate "fwd" when behavior = Calculator ->
                    do! appendToPromptAsync "<<"

                | Simulate "arrow_left" ->
                    do! writePromptAsync (prompt.Substring(0, prompt.Length - 1))

                | Press (Button "knob_push") when behavior = LoadPresetMulti ->
                    let num = prompt.Substring(2)
                        
                    do! clearAsync ()
                    do! Players.simulateButtonAsync player $"playPreset_{num}"

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

                | Press (Button "knob_push") when behavior = Calculator ->
                    let expression = prompt.Substring(2)

                    let parser = new Calcex.Parser()
                    let tree = parser.Parse(expression)
                    let result = tree.EvaluateDecimal()
                    do! Players.setDisplayAsync player "Result" $"{result}" (TimeSpan.FromSeconds(5))

                | Press (Button "exit_left") ->
                    do! clearAsync ()

                | _ -> ()
            })
        }

        let processNormalEntryAsync ircode time = task {
            let mappings =
                if powerState
                then MappingsOn
                else MappingsOff

            let mapping =
                mappings
                |> Map.tryFind ircode
                |> Option.defaultValue NoAction

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
                            if DateTime.UtcNow - start >= TimeSpan.FromSeconds(3) then
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

            | Press pressAction ->
                do! doOnceAsync ircode (fun () -> task {
                    do! pressAsync pressAction
                })

            | _ -> ()
        }

        let processIRAsync ircode time = task {
            holdTime.Value <- DateTime.UtcNow

            match promptText with
            | Some prompt -> do! processPromptEntryAsync ircode prompt
            | None -> do! processNormalEntryAsync ircode time
        }

        let processCommandAsync command = task {
            try
                match command with
                | [x; "playlist"; "newsong"; _; _] when Player x = player ->
                    channelChanging <- false
                | [x; "power"] when Player x = player ->
                    requestPowerState player
                | [x; "power"; "0"] when Player x = player ->
                    powerState <- false
                | [x; "power"; "1"] when Player x = player ->
                    powerState <- true
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

        let mutable handlers = Map.empty

        let init player =
            if not (handlers |> Map.containsKey player) then
                let handler = new Handler(player)
                handlers <- handlers |> Map.add player handler

                requestPowerState player

        override _.ExecuteAsync(cancellationToken) = task {
            use _ = reader |> Observable.subscribe (fun command ->
                match command with
                | [playerid; "client"; "new"]
                | [playerid; "client"; "reconnect"]
                | [playerid; "ir"; _; _]
                | [playerid; "unknownir"; _; _] ->
                    init (Player playerid)
                | _ -> ())

            try
                let! count = Players.countAsync ()
                for i in [0 .. count - 1] do
                    let! player = Players.getIdAsync i
                    init player
            with ex ->
                Console.Error.WriteLine(ex)

            do! Task.Delay(-1, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)
        }
