namespace RadioHomeEngine

open System
open FSharp.Control

module Discovery =
    let GetDiscInfoForInsertedDiscAsync() = taskSeq {
        printfn "[Discovery.fs] running icedax"
        let fromDisc = Icedax.getInfo ()
        if fromDisc.info.tracks.Length > 0 then
            let discIds = taskSeq {
                match fromDisc.discid with
                | Some discid -> discid
                | None -> ()

                printfn "[Discovery.fs] running abcde-musicbrainz-tool"
                try
                    let! discId2 = Abcde.GetMusicBrainzDiscIdAsync()
                    match discId2 with
                    | Some discid -> discid
                    | None -> ()
                with ex ->
                    Console.Error.WriteLine(ex)
            }

            for discId in discIds do
                printfn "[Discovery.fs] querying musicbrainz"
                try
                    let! result = MusicBrainz.GetInfoAsync(discId)
                    match result with
                    | Some info -> info
                    | None -> ()
                with ex ->
                    Console.Error.WriteLine(ex)

            fromDisc.info
    }
