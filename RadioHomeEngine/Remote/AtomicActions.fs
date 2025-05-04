namespace RadioHomeEngine

open System
open System.Diagnostics
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

        let! address = Network.getAddressAsync CancellationToken.None
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

            let! address = Network.getAddressAsync CancellationToken.None
            do! Playlist.playItemAsync player $"http://{address}:{Config.port}/Radio/PlayChannel?num={channelNumber}" $"[{channelNumber}] {name}"

        | PlayBrownNoise ->
            let! address = Network.getAddressAsync CancellationToken.None
            do! Playlist.playItemAsync player $"http://{address}:{Config.port}/Noise/playlist.m3u8" "Brown noise"

        | StreamInfo ->
            let! channelId = getCurrentSiriusXMChannelId player
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
                    do! Players.setDisplayAsync player "SiriusXM" "Please wait..." (TimeSpan.FromMilliseconds(1))
                | Some c ->
                    let! playlist = SiriusXMClient.getPlaylistAsync c.channelGuid c.channelId CancellationToken.None
                    let song =
                        playlist.cuts
                        |> Seq.sortByDescending (fun cut -> cut.startTime)
                        |> Seq.tryHead

                    match song with
                    | None ->
                        do! Players.setDisplayAsync player "SiriusXM" "Please wait..." (TimeSpan.FromMilliseconds(1))
                    | Some c ->
                        let artist = String.concat " / " c.artists
                        do! Players.setDisplayAsync player artist c.title (TimeSpan.FromSeconds(5))

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

        | Button name ->
            do! Players.simulateButtonAsync player name
    }
