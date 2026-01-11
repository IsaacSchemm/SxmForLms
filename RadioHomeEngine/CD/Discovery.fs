namespace RadioHomeEngine

open System
open FSharp.Control

module Discovery =
    let getInfoAsync driveNumbers = task {
        let mutable list = []

        for driveNumber in driveNumbers do
            printfn $"[Discovery.fs] [{driveNumber}] running icedax"

            let fromDisc = Icedax.getInfo driveNumber

            if fromDisc.info.tracks.Length > 0 then
                let discIds = taskSeq {
                    match fromDisc.discid with
                    | Some discid -> discid
                    | None -> ()

                    printfn $"[Discovery.fs] [{driveNumber}] running abcde-musicbrainz-tool"
                    try
                        let! discId2 = Abcde.getMusicBrainzDiscIdAsync driveNumber
                        match discId2 with
                        | Some discid -> discid
                        | None -> ()
                    with ex ->
                        Console.Error.WriteLine(ex)
                }

                let candidates = taskSeq {
                    for discId in discIds do
                        printfn $"[Discovery.fs] [{driveNumber}] querying musicbrainz"
                        try
                            let! result = MusicBrainz.getInfoAsync driveNumber discId
                            match result with
                            | Some info -> info
                            | None -> ()
                        with ex ->
                            Console.Error.WriteLine(ex)

                    fromDisc.info
                }

                let! singleCandidate =
                    candidates
                    |> TaskSeq.tryHead

                match singleCandidate with
                | Some c -> list <- c :: list
                | None -> ()

        return list
    }
