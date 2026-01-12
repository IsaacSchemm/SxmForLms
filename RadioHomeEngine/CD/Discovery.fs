namespace RadioHomeEngine

open System
open FSharp.Control

module Discovery =
    let private asyncGetDiscIds driveNumber driveInfo = asyncSeq {
        let icedax_id = driveInfo.discid

        match icedax_id with
        | Some id -> id
        | None -> ()

        try
            let! abcde_id = Abcde.getMusicBrainzDiscIdAsync driveNumber |> Async.AwaitTask

            match abcde_id with
            | Some id -> id
            | None -> ()
        with ex ->
            Console.Error.WriteLine(ex)
    }

    let private asyncQueryMusicBrainz discId = async {
        try
            return! MusicBrainz.getInfoAsync discId |> Async.AwaitTask
        with ex ->
            Console.Error.WriteLine(ex)
            return None
    }

    let asyncGetDiscInfo (driveNumber: int) = async {
        printfn $"[Discovery] [{driveNumber}] Scanning drive {driveNumber}..."

        let! driveInfo = Icedax.getInfoAsync driveNumber |> Async.AwaitTask
        let disc = driveInfo.disc

        if driveInfo.disc.tracks = [] then
            printfn $"[Discovery] [{driveNumber}] No tracks found on disc, not attempting MusicBrainz lookup"
            return driveInfo

        else if not (String.IsNullOrEmpty (disc.title)) then
            printfn $"[Discovery] [{driveNumber}] Using title {disc.title} from icedax"
            return driveInfo

        else
            printfn $"[Discovery] [{driveNumber}] Querying MusicBrainz..."

            let! candidate =
                asyncGetDiscIds driveNumber driveInfo
                |> AsyncSeq.chooseAsync asyncQueryMusicBrainz
                |> AsyncSeq.tryFirst

            match candidate with
            | Some newDisc ->
                printfn $"[Discovery] [{driveNumber}] Using title {newDisc.title} from MusicBrainz"
                return { driveInfo with disc = newDisc }
            | None ->
                printfn $"[Discovery] [{driveNumber}] Not found on MusicBrainz"
                printfn $"[Discovery] [{driveNumber}] Using title {disc.title} from icedax"
                return driveInfo
    }

    let getAllDiscInfoAsync (driveNumbers: int seq) = task {
        let! array =
            driveNumbers
            |> Seq.map asyncGetDiscInfo
            |> Async.Parallel

        return array
    }
