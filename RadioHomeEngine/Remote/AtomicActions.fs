namespace RadioHomeEngine

open System
open System.IO
open System.Text.RegularExpressions
open System.Threading
open FSharp.Control

open LyrionCLI

type AtomicAction =
| PlaySiriusXMChannel of int
| SiriusXMNowPlaying
| PlayAllDiscs
| RipAllDiscs
| EjectAllDiscs
| Forecast
| SeekBegin of decimal
| SeekCurrent of decimal
| Button of string

module AtomicActions =
    let getCurrentSiriusXMChannelId player = task {
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

    let playDiscsAsync player driveNumbers = task {
        do! Players.simulateButtonAsync player "stop"

        do! Players.setDisplayAsync player "Please wait" "Searching for tracks..." (TimeSpan.FromSeconds(999))

        let! info = Discovery.getInfoAsync driveNumbers

        do! Players.setDisplayAsync player "Please wait" "Finishing up..." (TimeSpan.FromMilliseconds(1))

        do! Playlist.clearAsync player

        let! address = Network.getAddressAsync ()
        for disc in info do
            for track in disc.tracks do
                let title =
                    match track.title with
                    | "" -> $"Track {track.position}"
                    | x -> x
                do! Playlist.addItemAsync player $"http://{address}:{Config.port}/CD/PlayTrack?driveNumber={disc.driveNumber}&track={track.position}" title

        do! Playlist.playAsync player
    }

    let performActionAsync player atomicAction = task {
        match atomicAction with
        | PlaySiriusXMChannel channelNumber ->
            let! channels = SiriusXMClient.getChannelsAsync CancellationToken.None
            let name =
                channels
                |> Seq.where (fun c -> c.channelNumber = $"{channelNumber}")
                |> Seq.map (fun c -> c.name)
                |> Seq.tryHead
                |> Option.defaultValue $"SiriusXM {channelNumber}"

            let! address = Network.getAddressAsync ()
            do! Playlist.playItemAsync player $"http://{address}:{Config.port}/SXM/PlayChannel?num={channelNumber}" $"[{channelNumber}] {name}"

        | SiriusXMNowPlaying ->
            do! Players.setDisplayAsync player "Info" "Please wait..." (TimeSpan.FromSeconds(5))

            let! nowPlaying = ChannelMemory.GetNowPlayingAsync(CancellationToken.None)

            let (line1, line2) =
                match nowPlaying with
                | [] -> ("Info", "No information found")
                | [a] -> ("Info", a)
                | a :: b :: _ -> (a, b)

            do! Players.setDisplayAsync player line1 line2 (TimeSpan.FromSeconds(5))

        | PlayAllDiscs ->
            do! playDiscsAsync player DiscDrives.allDriveNumbers

        | RipAllDiscs ->
            Abcde.beginRipAsync DiscDrives.allDriveNumbers

        | EjectAllDiscs ->
            for n in DiscDrives.allDriveNumbers do
                do! DiscDrives.ejectAsync n

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

        | SeekBegin time ->
            do! Playlist.setTimeAsync player SeekOrigin.Begin time

        | SeekCurrent time ->
            do! Playlist.setTimeAsync player SeekOrigin.Current time

        | Button name ->
            do! Players.simulateButtonAsync player name
    }
