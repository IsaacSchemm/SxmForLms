namespace RadioHomeEngine

open System
open FSharp.Control

module Discovery =
    let private asyncGetDiscIds driveNumber driveInfo = async {
        let mutable discIds = Set.empty

        let icedax_id = driveInfo.discid

        match icedax_id with
        | Some id -> discIds <- Set.add id discIds
        | None -> ()

        try
            let! abcde_id = Abcde.getMusicBrainzDiscIdAsync driveNumber |> Async.AwaitTask

            match abcde_id with
            | Some id -> discIds <- Set.add id discIds
            | _ -> ()
        with ex ->
            Console.Error.WriteLine(ex)

        return discIds
    }

    let private asyncQueryMusicBrainz discId = async {
        try
            let! result = MusicBrainz.getInfoAsync discId |> Async.AwaitTask
            return result
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

            let! discIds = asyncGetDiscIds driveNumber driveInfo

            let! candidates =
                discIds
                |> Seq.map asyncQueryMusicBrainz
                |> Async.Parallel

            let candidate =
                candidates
                |> Seq.choose id
                |> Seq.tryHead

            match candidate with
            | Some newDisc ->
                printfn $"[Discovery] [{driveNumber}] Using title {newDisc.title} from MusicBrainz"
                return { driveInfo with disc = newDisc }
            | None ->
                printfn $"[Discovery] [{driveNumber}] Not found on MusicBrainz"
                printfn $"[Discovery] [{driveNumber}] Using title {disc.title} from icedax"
                return driveInfo
    }

    let getAllDiscInfoAsync scope = task {
        let! array =
            scope
            |> DiscDrives.getDriveNumbers
            |> Seq.map asyncGetDiscInfo
            |> Async.Parallel

        return array
    }
