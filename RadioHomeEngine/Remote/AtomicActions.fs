namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Text.RegularExpressions
open System.Threading

open LyrionCLI

type AtomicAction =
| PlaySiriusXMChannel of int
| PlayBrownNoise
| StreamInfo
| PlayTrack of int
| Eject
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

    let playAllTracksAsync player (startAtTrack: int) = task {
        do! Players.simulateButtonAsync player "stop"

        do! Players.setDisplayAsync player "Please wait" "Searching for tracks..." (TimeSpan.FromSeconds(999))

        let disc = Icedax.getInfo ()

        do! Players.setDisplayAsync player "Please wait" "Searching for tracks..." (TimeSpan.FromMilliseconds(1))

        do! Playlist.clearAsync player

        let! address = Network.getAddressAsync ()
        for track in disc.tracks |> Seq.skipWhile (fun t -> t.number < startAtTrack) do
            let title =
                match track.title with
                | "" -> $"Track {track.number}"
                | x -> x
            do! Playlist.addItemAsync player $"http://{address}:{Config.port}/CD/PlayTrack?track={track.number}" title

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
            do! Playlist.playItemAsync player $"http://{address}:{Config.port}/Radio/PlayChannel?num={channelNumber}" $"[{channelNumber}] {name}"

        | PlayBrownNoise ->
            let! address = Network.getAddressAsync ()
            do! Playlist.playItemAsync player $"http://{address}:{Config.port}/Noise/playlist.m3u8" "Brown noise"

        | StreamInfo ->
            do! Players.setDisplayAsync player "Info" "Please wait..." (TimeSpan.FromSeconds(5))

            let! nowPlaying = ChannelMemory.GetNowPlayingAsync(CancellationToken.None)

            let (line1, line2) =
                match nowPlaying with
                | [] -> ("Info", "No information found")
                | [a] -> ("Info", a)
                | a :: b :: _ -> (a, b)

            do! Players.setDisplayAsync player line1 line2 (TimeSpan.FromSeconds(5))

        | Eject ->
            use proc = Process.Start("eject", $"-T {Icedax.device}")
            do! proc.WaitForExitAsync()

        | PlayTrack n ->
            do! playAllTracksAsync player n

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
