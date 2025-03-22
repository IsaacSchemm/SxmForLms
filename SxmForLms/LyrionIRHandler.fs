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

    let getTitle behavior =
        match behavior with
        | Digit -> "Direct digit entry"
        | LoadPresetSingle -> "Load preset (single-digit)"
        | LoadPresetMulti -> "Load preset (multi-digit)"
        | SeekTo -> "Seek (ss/mm:ss/hh:mm:ss)"
        | SiriusXM -> "Play SiriusXM channel"

    let (|IRCode|_|) (str: string) =
        match Int32.TryParse(str, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) with
        | true, value -> Some value
        | false, _ -> None

    type Power = On | Off

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

    type Prompter(player: Player) =
        let mutable written = None
        let mutable currentTask = Task.CompletedTask

        member val Behavior = Digit with get, set

        member _.CurrentText = written

        member this.WriteAsync(text) = task {
            let header = getTitle this.Behavior

            written <- Some text
            do! Players.setDisplayAsync player header text (TimeSpan.FromSeconds(10))

            if currentTask.IsCompleted then currentTask <- task {
                printf "Monitoring remote screen..."
                while written <> None do
                    printf "."
                    do! Task.Delay(200)
                    let! (current, _) = Players.getDisplayNowAsync player
                    if current <> getTitle this.Behavior then
                        printfn " screen reset."
                        written <- None
            }
        }

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

        let mutable channelChanging = false

        let playSiriusXMChannelAsync channelNumber name = task {
            let! address = Network.getAddressAsync CancellationToken.None
            let url = $"http://{address}:{Config.port}/Radio/PlayChannel?num={channelNumber}"
            let name = $"[{channelNumber}] {name}"
            do! Playlist.playItemAsync player url name
            channelChanging <- true
        }

        let mutable powerState = Off

        let mutable holdTime = ref DateTime.UtcNow

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

        let prompter = new Prompter(player)

        let clearAsync () = task {
            do! Players.setDisplayAsync player " " " " (TimeSpan.FromMilliseconds(1))
        }

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
                    Digit
                }

                let! (header, _) = Players.getDisplayNowAsync player
                if header = "Input Mode" then
                    prompter.Behavior <-
                        Seq.pairwise all
                        |> Seq.where (fun (a, _) -> a = prompter.Behavior)
                        |> Seq.map (fun (_, b) -> b)
                        |> Seq.head

                do! Players.setDisplayAsync player "Input Mode" (getTitle prompter.Behavior) (TimeSpan.FromSeconds(3))
        }

        let processPromptEntryAsync ircode (prompt: string) = task {
            let mappings =
                if powerState = On
                then MappingsOn
                else MappingsOff

            let mapping =
                mappings
                |> Map.tryFind ircode
                |> Option.defaultValue NoAction

            match mapping with
                | Simulate str when str.Length = 1 && "0123456789".Contains(str) ->
                    do! doOnceAsync ircode (fun () -> task {
                        do! prompter.WriteAsync($"{prompt}{str}")
                    })

                | Dot when prompter.Behavior = SeekTo ->
                    do! doOnceAsync ircode (fun () -> task {
                        do! prompter.WriteAsync($"{prompt}:")
                    })

                | Simulate "arrow_left" ->
                    do! doOnceAsync ircode (fun () -> task {
                        if prompt.Length > 2 then
                            do! prompter.WriteAsync(prompt.Substring(0, prompt.Length - 1))
                    })

                | Press (Button "knob_push") when prompter.Behavior = SiriusXM ->
                    do! doOnceAsync ircode (fun () -> task {
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
                            do! prompter.WriteAsync("> ")
                    })

                | Press (Button "knob_push") when prompter.Behavior = LoadPresetMulti ->
                    do! doOnceAsync ircode (fun () -> task {
                        let num = prompt.Substring(2)
                        
                        do! clearAsync ()
                        do! Players.simulateButtonAsync player $"playPreset_{num}"
                    })

                | Press (Button "knob_push") when prompter.Behavior = SeekTo ->
                    do! doOnceAsync ircode (fun () -> task {
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
                            do! prompter.WriteAsync("> ")
                    })

                | Press (Button "exit_left") ->
                    do! doOnceAsync ircode (fun () -> task {
                        do! clearAsync ()
                    })

                | _ -> ()
        }

        let processNormalEntryAsync ircode time = task {
            let mappings =
                if powerState = On
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
                        | OnRelease pressAction -> do! pressAsync pressAction
                        | _ -> ()
                })

            | Simulate str when str.Length = 1 && "0123456789".Contains(str) && prompter.Behavior <> Digit ->
                do! doOnceAsync ircode (fun () -> task {
                    if prompter.Behavior = LoadPresetSingle then
                        do! Players.simulateButtonAsync player $"playPreset_{str}"
                    else
                        do! prompter.WriteAsync($"> {str}")
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

            match prompter.CurrentText with
            | Some prompt -> do! processPromptEntryAsync ircode prompt
            | None -> do! processNormalEntryAsync ircode time
        }

        let processCommandAsync command = task {
            try
                match command with
                | [x; "playlist"; "newsong"; _; _] when Player x = player ->
                    channelChanging <- false
                | [x; "power"] when Player x = player ->
                    do! Players.getPowerAsync player :> Task
                | [x; "power"; "0"] when Player x = player ->
                    powerState <- Off
                | [x; "power"; "1"] when Player x = player ->
                    powerState <- On
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
                ignore (Players.getPowerAsync player)

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
