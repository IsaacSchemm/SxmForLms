namespace RadioHomeEngine

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open FSharp.Control

open LyrionCLI

type AtomicAction =
| PlaySiriusXMChannel of int
| Information
| PlayPause
| Replay
| PlayCD of DiscDriveScope
| RipCD of DiscDriveScope
| EjectCD of DiscDriveScope
| Forecast

module AtomicActions =
    let zeroCodes = [
        ("0", Information, "Information")
        ("01", PlayCD AllDrives, "Play CD")
        ("03", EjectCD AllDrives, "Eject CD")
        ("07", RipCD AllDrives, "Rip CD")
        ("09", Forecast, "Weather")
    ]

    let tryGetAction entry = Seq.tryHead (seq {
        if entry = "0" then
            Information

        for num, action, _ in zeroCodes do
            if num = entry then
                action

        match entry with
        | Int32 n -> PlaySiriusXMChannel n
        | _ -> ()
    })

    let performActionAsync player atomicAction = task {
        match atomicAction with
        | PlaySiriusXMChannel channelNumber ->
            let! channels = SiriusXMClient.getChannelsAsync CancellationToken.None
            let name =
                channels
                |> Seq.where (fun c -> c.channelNumber = $"{channelNumber}")
                |> Seq.map (fun c -> c.name)
                |> Seq.tryHead

            match name with
            | None -> ()
            | Some channelName ->
                let! address = Network.getAddressAsync ()
                do! Playlist.playItemAsync player $"http://{address}:{Config.port}/SXM/PlayChannel?num={channelNumber}" $"[{channelNumber}] {channelName}"

        | Information ->
            let sec n = TimeSpan.FromSeconds(n)
            let wait n = Task.Delay(sec n)
            let title = "Information"

            do! Players.setDisplayAsync player title "1-999: SiriusXM" (sec 10)
            do! wait 2

            for code, _, name in zeroCodes do
                do! Players.setDisplayAsync player title $"{code}: {name}" (sec 10)
                do! wait 2

            match player with Player id ->
                do! Players.setDisplayAsync player title $"Player ID: {id}" (sec 10)
                do! wait 5

            let! ip = Network.getAddressAsync ()
            do! Players.setDisplayAsync player title $"Server: {ip}:{Config.port}" (sec 5)

        | PlayPause ->
            let! state = Playlist.getModeAsync player
            match state with
            | Playlist.Mode.Paused -> do! Playlist.setPauseAsync player false
            | Playlist.Mode.Playing -> do! Playlist.setPauseAsync player true
            | Playlist.Mode.Stopped -> do! Playlist.playAsync player

        | Replay ->
            do! Playlist.setTimeAsync player SeekOrigin.Current -10m

        | PlayCD scope ->
            do! Players.simulateButtonAsync player "stop"

            do! Players.setDisplayAsync player "Please wait" "Searching for tracks..." (TimeSpan.FromSeconds(999))

            let! drives = Discovery.getAllDiscInfoAsync scope

            do! Players.setDisplayAsync player "Please wait" "Finishing up..." (TimeSpan.FromMilliseconds(1))

            do! Playlist.clearAsync player

            let! address = Network.getAddressAsync ()
            for driveInfo in drives do
                for track in driveInfo.disc.tracks do
                    let title =
                        match track.title with
                        | "" -> $"Track {track.position}"
                        | x -> x
                    do! Playlist.addItemAsync player $"http://{address}:{Config.port}/CD/PlayTrack?driveNumber={driveInfo.driveNumber}&track={track.position}" title

            do! Playlist.playAsync player

        | RipCD scope ->
            Abcde.beginRipAsync scope

        | EjectCD scope ->
            do! DiscDrives.ejectAsync scope

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
    }
