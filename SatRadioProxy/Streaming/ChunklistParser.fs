namespace SatRadioProxy.Streaming

open System

open SatRadioProxy

module ChunklistParser =
    type Segment = {
        key: string
        headerTags: string list
        mediaSequence: UInt128
        segmentTags: string list
        path: string
    }

    let (|UInt128|_|) (str: string) =
        match UInt128.TryParse(str) with
        | true, value -> Some value
        | false, _ -> None

    let zero = UInt128.Zero
    let one = UInt128.One

    let (|Tag|_|) (str: string) =
        match str.StartsWith('#'), str.IndexOf(':') with
        | false, _
        | true, -1 -> None
        | true, index ->
            let name = str.Substring(1, index - 1)
            let value = str.Substring(index + 1)
            Some (name, value)

    let isSegmentTag name =
        match name with
        | "EXTINF"
        | "EXT-X-BYTERANGE"
        | "EXT-X-PROGRAM-DATE-TIME" -> true
        | _ -> false

    let parse text = [
        let mutable key = "NONE"
        let mutable headerTags = []
        let mutable segmentTags = []

        let mutable mediaSequence = zero

        for line in Utility.split '\n' text do
            match line with
            | Tag ("EXT-X-KEY", value) ->
                key <- value
            | Tag ("EXT-X-MEDIA-SEQUENCE", UInt128 value) ->
                mediaSequence <- value
            | Tag ("EXTINF", _)
            | Tag ("EXT-X-BYTERANGE", _)
            | Tag ("EXT-X-PROGRAM-DATE-TIME", _) ->
                segmentTags <- List.rev (line :: segmentTags)
            | Tag _ ->
                headerTags <- List.rev (line :: headerTags)
            | _ when not (line.StartsWith('#')) ->
                {
                    key = key
                    headerTags = headerTags
                    mediaSequence = mediaSequence
                    segmentTags = segmentTags
                    path = line
                }
                segmentTags <- []
                mediaSequence <- mediaSequence + one
            | _ -> ()
    ]

    let write segments = String.concat "\n" [
        "#EXTM3U"

        match Seq.tryHead segments with
        | None -> ()
        | Some segment ->
            yield! segment.headerTags

            if segment.mediaSequence <> zero then
                $"#EXT-X-MEDIA-SEQUENCE:{segment.mediaSequence}"

        let mutable lastKey = "NONE"

        for segment in segments do
            if segment.key <> lastKey then
                $"EXT-X-KEY:{segment.key}"
                lastKey <- segment.key

            yield! segment.segmentTags

            segment.path
    ]
