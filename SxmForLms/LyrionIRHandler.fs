namespace SxmForLms

open System
open System.Globalization
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

    type Behavior =
    | Normal
    | EnterPresetBehavior of text: string
    | EnterSiriusXMChannelBehavior of text: string

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
            | EnterPresetBehavior text ->
                do! Players.setDisplayAsync player "Enter Preset" text expirationTimeSpan
            | EnterSiriusXMChannelBehavior text ->
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

            | true, Normal, Simulate name ->
                do! Players.simulateIRAsync player Slim[name] time

            | true, Normal, Button button ->
                do! doOnceAsync ircode (fun () -> task {
                    do! Players.simulateButtonAsync player button
                })

            | true, Normal, EnterPreset ->
                do! doOnceAsync ircode (fun () -> task {
                    do! setModeAsync (EnterPresetBehavior "")
                })

            | true, EnterPresetBehavior text, Simulate n when n.Length = 1 && "0123456789".Contains(n) ->
                do! doOnceAsync ircode (fun () -> task {
                    do! setModeAsync (EnterPresetBehavior $"{text}{n}")
                })

            | true, EnterPresetBehavior preset, Button "knob_push" ->
                do! doOnceAsync ircode (fun () -> task {
                    do! Players.simulateButtonAsync player $"playPreset_{preset}"
                    do! clearModeAsync ()
                })

            | true, Normal, EnterSiriusXMChannel ->
                do! doOnceAsync ircode (fun () -> task {
                    do! setModeAsync (EnterSiriusXMChannelBehavior "")
                })

            | true, EnterSiriusXMChannelBehavior text, Simulate n when n.Length = 1 && "0123456789".Contains(n) ->
                do! doOnceAsync ircode (fun () -> task {
                    do! setModeAsync (EnterSiriusXMChannelBehavior $"{text}{n}")
                })

            | true, EnterSiriusXMChannelBehavior number, Button "knob_push" ->
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

            | true, EnterPresetBehavior _, Button "exit_left"
            | true, EnterSiriusXMChannelBehavior _, Button "exit_left" ->
                do! clearModeAsync ()

            | _ -> ()
        }

        let processCommandAsync command = task {
            match command with
            | [x; "power"; "0"] when Player x = player ->
                power <- false
            | [x; "power"; "1"] when Player x = player ->
                power <- true
            | [x; "unknownir"; IRCode ircode; Decimal time] when Player x = player ->
                do! processIRAsync ircode time
            | _ -> ()
        }

        let subscriber = reader |> Observable.subscribe (fun command ->
            (processCommandAsync command).GetAwaiter().GetResult())

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
