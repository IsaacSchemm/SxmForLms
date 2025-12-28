namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Text.RegularExpressions
open System.Threading

open LyrionCLI

type AtomicAction =
| PlaySiriusXMChannel of int
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

    let tryQueryMusicDatabaseAsync discId trackCount = task {
        try
            match discId with
            | Some d ->
                let! result = MusicBrainz.GetTracksAsync(d)
                if List.length result.tracks <> trackCount then
                    failwithf "Track count mismatch: %d tracks on album, but MusicBrainz has %d" trackCount (List.length result.tracks)

                return Some result
            | None ->
                return failwith "No discId found, cannot query MusicBrainz"
        with ex ->
            Console.Error.WriteLine(ex)
            return None
    }

    let playAllTracksAsync player (startAtTrack: int) = task {
        do! Players.simulateButtonAsync player "stop"

        do! Players.setDisplayAsync player "Please wait" "Searching for tracks..." (TimeSpan.FromSeconds(999))

        let disc = Icedax.getInfo ()

        do! Players.setDisplayAsync player "Please wait" "Querying online database..." (TimeSpan.FromMilliseconds(1))

        let! tracks = task {
            try
                match disc.discid with
                | Some d ->
                    let! result = MusicBrainz.GetTracksAsync(d)
                    if List.length result.tracks <> List.length disc.tracks then
                        failwithf "Track count mismatch: %d tracks on album, but MusicBrainz has %d" (List.length disc.tracks) (List.length result.tracks)

                    return [
                        for t in result.tracks do {|
                            title = t.title
                            number = t.position
                        |}
                    ]
                | None ->
                    return failwith "No discId found, cannot query MusicBrainz"
            with ex ->
                Console.Error.WriteLine(ex)
                return disc.tracks
        }

        do! Players.setDisplayAsync player "Please wait" "Finishing up..." (TimeSpan.FromMilliseconds(1))

        do! Playlist.clearAsync player

        let! address = Network.getAddressAsync ()
        for track in tracks |> Seq.skipWhile (fun t -> t.number < startAtTrack) do
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
            do! Playlist.playItemAsync player $"http://{address}:{Config.port}/SXM/PlayChannel?num={channelNumber}" $"[{channelNumber}] {name}"

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
