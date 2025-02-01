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
    let (|IRCode|_|) (str: string) =
        match Int32.TryParse(str, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) with
        | true, value -> Some value
        | false, _ -> None

    let proxyPathPattern = new Regex("""^http://[^/]+:[0-9]+/Proxy/playlist-(.+)\.m3u8""")

    let (|ProxyPath|_|) (str: string) =
        let m = proxyPathPattern.Match(str)
        if m.Success then
            Some m.Groups[1].Value
        else
            None

    type Behavior =
    | Normal
    | PresetEntry of text: string
    | SiriusXMEntry of text: string

    type Handler(player: Player) =
        let mutable power = false

        let mutable buttonsPressed = Map.empty

        let mutable lastMode = None

        let setModeAsync behavior = task {
            let expirationTimeSpan = TimeSpan.FromSeconds(15)

            lastMode <- Some {|
                behavior = behavior
                expires = DateTime.UtcNow + expirationTimeSpan
            |}

            match behavior with
            | Normal -> ()
            | PresetEntry text ->
                do! Players.setDisplayAsync player "Enter Preset" text expirationTimeSpan
            | SiriusXMEntry text ->
                do! Players.setDisplayAsync player "Enter SiriusXM Channel Number" text expirationTimeSpan
        }

        let clearModeAsync () = task {
            lastMode <- None

            do! Players.setDisplayAsync player "" "" (TimeSpan.FromSeconds(0.05))
        }

        let doOnceAsync ircode action = task {
            let now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

            let lastPressedAt =
                buttonsPressed
                |> Map.tryFind ircode
                |> Option.defaultValue 0L

            let wasPressed = lastPressedAt > now - 150L

            buttonsPressed <-
                buttonsPressed
                |> Map.add ircode now

            if not wasPressed then
                do! action ()
        }

        let processIRAsync ircode time = task {
            let behavior =
                lastMode
                |> Option.filter (fun m -> m.expires > DateTime.UtcNow)
                |> Option.map (fun m -> m.behavior)
                |> Option.defaultValue Normal

            let mapping =
                CustomMappings
                |> Map.tryFind ircode
                |> Option.defaultValue LyrionIR.NoAction

            match power, behavior, mapping with
            | _, _, Power ->
                do! doOnceAsync ircode (fun () -> task {
                    do! Players.togglePowerAsync player
                })

            | true, Normal, Info ->
                do! doOnceAsync ircode (fun () -> task {
                    let mutable handled = false

                    let! path = Playlist.getPathAsync player

                    match Uri.TryCreate(path, UriKind.Absolute) with
                    | false, _ -> ()
                    | true, uri ->
                        let proxyPathPattern = new Regex("""^/Proxy/playlist-(.+)\.m3u8$""")
                        let m = proxyPathPattern.Match(uri.AbsolutePath)

                        if m.Success then
                            let! channels = SiriusXMClient.getChannelsAsync CancellationToken.None
                            for c in channels do
                                if c.channelId = m.Groups[1].Value then
                                    let! playlist = SiriusXMClient.getPlaylistAsync c.channelGuid c.channelId CancellationToken.None
                                    let cut =
                                        playlist.cuts
                                        |> Seq.sortByDescending (fun cut -> cut.startTime)
                                        |> Seq.tryHead
                                    match cut with
                                    | None -> ()
                                    | Some c ->
                                        let artist = String.concat " / " c.artists
                                        do! Players.setDisplayAsync player artist c.title (TimeSpan.FromSeconds(10))
                                        handled <- true

                    if not handled then
                        do! Players.simulateButtonAsync player "now_playing"
                })

            | true, Normal, EnterPreset ->
                do! doOnceAsync ircode (fun () -> task {
                    do! setModeAsync (PresetEntry "")
                })

            | true, PresetEntry text, Simulate n when n.Length = 1 && "0123456789".Contains(n) ->
                do! doOnceAsync ircode (fun () -> task {
                    do! setModeAsync (PresetEntry $"{text}{n}")
                })

            | true, PresetEntry preset, Button "knob_push" ->
                do! doOnceAsync ircode (fun () -> task {
                    do! Players.simulateButtonAsync player $"playPreset_{preset}"
                    do! clearModeAsync ()
                })

            | true, Normal, EnterSiriusXMChannel ->
                do! doOnceAsync ircode (fun () -> task {
                    do! setModeAsync (SiriusXMEntry "")
                })

            | true, SiriusXMEntry text, Simulate n when n.Length = 1 && "0123456789".Contains(n) ->
                do! doOnceAsync ircode (fun () -> task {
                    do! setModeAsync (SiriusXMEntry $"{text}{n}")
                })

            | true, SiriusXMEntry number, Button "knob_push" ->
                do! doOnceAsync ircode (fun () -> task {
                    //let! address = Network.getAddressAsync CancellationToken.None
                    let address = "192.168.4.36"
                    let! channels = SiriusXMClient.getChannelsAsync CancellationToken.None
                    let channelName =
                        channels
                        |> Seq.where (fun c -> c.channelNumber = number)
                        |> Seq.map (fun c -> c.name)
                        |> Seq.tryHead
                        |> Option.defaultValue number
                    do! Playlist.playItemAsync player $"http://{address}:{Config.port}/Radio/PlayChannel?num={number}" channelName
                    do! clearModeAsync ()
                })

            | true, _, Simulate name ->
                if behavior <> Normal then
                    do! clearModeAsync ()

                do! Players.simulateIRAsync player Slim[name] time

            | true, _, Button button ->
                if behavior <> Normal then
                    do! clearModeAsync ()

                do! doOnceAsync ircode (fun () -> task {
                    do! Players.simulateButtonAsync player button
                })

            | _ -> ()
        }

        let processCommandAsync command = task {
            try
                match command with
                | [x; "power"; "0"] when Player x = player ->
                    power <- false
                | [x; "power"; "1"] when Player x = player ->
                    power <- true
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

                ignore (Players.getPowerAsync(player))

        override _.ExecuteAsync(cancellationToken) = task {
            use _ = reader |> Observable.subscribe (fun command ->
                match command with
                | [playerid; "client"; "new"]
                | [playerid; "client"; "reconnect"] ->
                    init (Player playerid)
                | _ -> ())

            let! count = Players.countAsync ()
            for i in [0 .. count - 1] do
                let! player = Players.getIdAsync i
                init player

            do! Task.Delay(-1, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)
        }
