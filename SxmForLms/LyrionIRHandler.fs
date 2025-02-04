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

    type Power =
    | On
    | Off

    type Behavior =
    | Normal
    | Override of string * string

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

    type Handler(player: Player) =
        let mutable power = Off

        let mutable buttonsPressed = Map.empty
        let mutable channelChanging = false
        let mutable lastDisplay = Normal

        let setDisplayAsync line1 line2 = task {
            lastDisplay <- Override (line1, line2)
            do! Players.setDisplayAsync player line1 line2 (TimeSpan.FromSeconds(30))
        }

        let checkDisplayAsync () = task {
            match lastDisplay with
            | Normal -> ()
            | Override (line1, line2) ->
                let! actual = Players.getDisplayNowAsync player
                if (line1, line2) <> actual then
                    lastDisplay <- Normal
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

        let playSiriusXMChannelAsync channelNumber name = task {
            let! address = Network.getAddressAsync CancellationToken.None
            let url = $"http://{address}:{Config.port}/Radio/PlayChannel?num={channelNumber}"
            let name = $"[{channelNumber}] {name}"
            do! Playlist.playItemAsync player url name
            channelChanging <- true
        }

        let processIRAsync ircode time = task {
            do! checkDisplayAsync ()

            let mapping =
                CustomMappings
                |> Map.tryFind ircode
                |> Option.defaultValue NoAction

            match power, lastDisplay, mapping with
            | _, _, Power ->
                do! doOnceAsync ircode (fun () -> task {
                    do! Players.togglePowerAsync player
                })

            | Off, _, _ ->
                printfn "Radio is off"

            | On, _, NoAction ->
                printfn "Button is not mapped"

            | On, Normal, Info ->
                do! doOnceAsync ircode (fun () -> task {
                    let mutable handled = false

                    let! id = SXM.getChannelIdAsync player

                    match id with
                    | None -> ()
                    | Some i ->
                        let! channels = SiriusXMClient.getChannelsAsync CancellationToken.None
                        for c in channels do
                            if c.channelId = i then
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

            | On, Normal, ChannelUp ->
                if not channelChanging then
                    do! doOnceAsync ircode (fun () -> task {
                        let! id = SXM.getChannelIdAsync player

                        match id with
                        | None -> ()
                        | Some i ->
                            let! channels = SiriusXMClient.getChannelsAsync CancellationToken.None

                            for (a, b) in Seq.pairwise channels do
                                if a.channelId = i then
                                    do! playSiriusXMChannelAsync b.channelNumber b.name
                    })

            | On, Normal, ChannelDown ->
                if not channelChanging then
                    do! doOnceAsync ircode (fun () -> task {
                        let! channelNumber = SXM.getChannelIdAsync player

                        match channelNumber with
                        | None -> ()
                        | Some i ->
                            let! channels = SiriusXMClient.getChannelsAsync CancellationToken.None

                            for (a, b) in Seq.pairwise channels do
                                if b.channelId = i then
                                    do! playSiriusXMChannelAsync a.channelNumber a.name
                    })

            | On, Normal, Exit ->
                do! doOnceAsync ircode (fun () -> task {
                    do! Players.simulateButtonAsync player "exit_left"
                })

            | On, Normal, Simulate name ->
                do! Players.simulateIRAsync player Slim[name] time

            | On, Normal, Button button ->
                do! doOnceAsync ircode (fun () -> task {
                    do! Players.simulateButtonAsync player button
                })

            | On, Normal, Input ->
                do! doOnceAsync ircode (fun () -> task {
                    do! setDisplayAsync "Play SiriusXM Channel" "> "
                })

            | On, Override ("Play SiriusXM Channel", text), Simulate str when str.Length = 1 && "0123456789".Contains(str) ->
                do! doOnceAsync ircode (fun () -> task {
                    do! setDisplayAsync "Play SiriusXM Channel" $"{text}{str}"
                })

            | On, Override ("Play SiriusXM Channel", text), Simulate "arrow_left" when text <> "> " ->
                do! doOnceAsync ircode (fun () -> task {
                    do! setDisplayAsync "Play SiriusXM Channel" $"{text.Substring(0, text.Length - 1)}"
                })

            | On, Override ("Play SiriusXM Channel", text), Button "knob_push" ->
                do! doOnceAsync ircode (fun () -> task {
                    let num = text.Substring(2)

                    let! channels = SiriusXMClient.getChannelsAsync CancellationToken.None
                    let channel =
                        channels
                        |> Seq.where (fun c -> c.channelNumber = num)
                        |> Seq.tryHead

                    match channel with
                    | Some c ->
                        do! playSiriusXMChannelAsync c.channelNumber c.name
                    | None ->
                        do! setDisplayAsync "Play SiriusXM Channel" "> "
                })

            | On, Override ("Play SiriusXM Channel", _), Input ->
                do! doOnceAsync ircode (fun () -> task {
                    do! setDisplayAsync "Load Preset" "> "
                })

            | On, Override ("Load Preset", text), Simulate str when str.Length = 1 && "0123456789".Contains(str) ->
                do! doOnceAsync ircode (fun () -> task {
                    do! setDisplayAsync "Load Preset" $"{text}{str}"
                })

            | On, Override ("Load Preset", text), Simulate "arrow_left" when text <> "> " ->
                do! doOnceAsync ircode (fun () -> task {
                    do! setDisplayAsync "Load Preset" $"{text.Substring(0, text.Length - 1)}"
                })

            | On, Override ("Load Preset", text), Button "knob_push" ->
                do! doOnceAsync ircode (fun () -> task {
                    let num = text.Substring(2)

                    do! Players.simulateButtonAsync player $"playPreset_{num}"
                })

            | On, Override ("Load Preset", _), Input ->
                do! doOnceAsync ircode (fun () -> task {
                    do! setDisplayAsync "Save Preset" "> "
                })

            | On, Override ("Save Preset", text), Simulate str when str.Length = 1 && "0123456789".Contains(str) ->
                do! doOnceAsync ircode (fun () -> task {
                    do! setDisplayAsync "Save Preset" $"{text}{str}"
                })

            | On, Override ("Save Preset", text), Simulate "arrow_left" when text <> "> " ->
                do! doOnceAsync ircode (fun () -> task {
                    do! setDisplayAsync "Save Preset" $"{text.Substring(0, text.Length - 1)}"
                })

            | On, Override ("Save Preset", text), Button "knob_push" ->
                do! doOnceAsync ircode (fun () -> task {
                    let num = text.Substring(2)

                    do! Players.simulateButtonAsync player $"favorites_add{num}"
                })

            | On, Override ("Save Preset", _), Input ->
                do! doOnceAsync ircode (fun () -> task {
                    do! Players.setDisplayAsync player " " " " (TimeSpan.FromSeconds(0.001))
                    lastDisplay <- Normal
                })

            | On, Override _, Exit ->
                do! doOnceAsync ircode (fun () -> task {
                    do! Players.setDisplayAsync player " " " " (TimeSpan.FromSeconds(0.001))
                    lastDisplay <- Normal
                })

            | On, Override _, _ ->
                printfn "Button is ignored"
        }

        let processCommandAsync command = task {
            try
                match command with
                | [x; "playlist"; "newsong"; _; _] when Player x = player ->
                    channelChanging <- false
                | [x; "power"; "0"] when Player x = player ->
                    power <- Off
                | [x; "power"; "1"] when Player x = player ->
                    power <- On
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
