namespace RadioHomeEngine

open System
open FSharp.Control

module Discovery =
    let private asyncGetDiscIds device driveInfo = asyncSeq {
        let icedax_id = driveInfo.discid

        match icedax_id with
        | Some id -> yield id
        | None -> ()

        try
            let! abcde_id = Abcde.getMusicBrainzDiscIdAsync device |> Async.AwaitTask

            match abcde_id with
            | Some id -> yield id
            | _ -> ()
        with ex ->
            Console.Error.WriteLine(ex)
    }

    let private asyncQueryMusicBrainz discId = async {
        try
            printfn $"[Discovery] Querying MusicBrainz for disc {discId}..."
            let! result = MusicBrainz.getInfoAsync discId |> Async.AwaitTask
            return result
        with ex ->
            Console.Error.WriteLine(ex)
            return None
    }

    let asyncGetDiscInfo device = async {
        printfn $"[Discovery] [{device}] Scanning drive {device}..."

        let! driveInfo = Icedax.getInfoAsync device |> Async.AwaitTask
        let disc = driveInfo.disc

        if driveInfo.disc.tracks = [] then
            printfn $"[Discovery] [{device}] No tracks found on disc, not attempting MusicBrainz lookup"
            return driveInfo

        else if Option.isSome disc.title then
            printfn $"[Discovery] [{device}] Using title {disc.title} from icedax"
            return driveInfo

        else
            printfn $"[Discovery] [{device}] Preparing to query MusicBrainz..."

            let! candidate =
                asyncGetDiscIds device driveInfo
                |> AsyncSeq.distinctUntilChanged
                |> AsyncSeq.mapAsync asyncQueryMusicBrainz
                |> AsyncSeq.choose id
                |> AsyncSeq.tryFirst

            match candidate with
            | Some newDisc ->
                printfn $"[Discovery] [{device}] Using title {newDisc.title} from MusicBrainz"
                return { driveInfo with disc = newDisc }
            | None ->
                printfn $"[Discovery] [{device}] Not found on MusicBrainz"
                printfn $"[Discovery] [{device}] Using title {disc.title} from icedax"
                return driveInfo
    }

    let getAllDiscInfoAsync scope = task {
        let! array =
            scope
            |> DiscDrives.getDevices
            |> Seq.map asyncGetDiscInfo
            |> Async.Parallel

        return Array.toList array
    }
