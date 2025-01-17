namespace SatRadioProxy

open System

module ChunklistParser =
    let (|Int128|_|) (str: string) =
        match Int128.TryParse(str) with
        | true, value -> Some value
        | false, _ -> None

    let zero = Int128.Zero
    let one = Int128.One

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

    type Segment = {
        keyTag: string option
        headerTags: string list
        mediaSequence: Int128
        segmentTags: string list
        path: string
    }

    let parse text = [
        let mutable keyTag = None
        let mutable headerTags = []
        let mutable segmentTags = []

        let mutable mediaSequence = zero

        for line in Utility.split '\n' text do
            match line with
            | Tag ("EXT-X-KEY", value) ->
                keyTag <- Some value
            | Tag ("EXT-X-MEDIA-SEQUENCE", Int128 value) ->
                mediaSequence <- value
            | Tag ("EXTINF", _)
            | Tag ("EXT-X-BYTERANGE", _)
            | Tag ("EXT-X-PROGRAM-DATE-TIME", _) ->
                segmentTags <- line :: segmentTags
            | Tag _ ->
                headerTags <- line :: headerTags
            | _ when not (line.StartsWith('#')) ->
                {
                    keyTag = keyTag
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
            yield! List.rev segment.headerTags

            if segment.mediaSequence <> zero then
                $"#EXT-X-MEDIA-SEQUENCE:{segment.mediaSequence}"

            match segment.keyTag with
            | Some key -> $"#EXT-X-KEY:{key}"
            | None -> ()

        for segment in segments do
            yield! List.rev segment.segmentTags
            segment.path
    ]
