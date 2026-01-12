namespace RadioHomeEngine

open System
open System.Threading.Tasks
open FSharp.Control

module Discovery =
    let private getDiscIdsAsync driveNumber driveInfo = taskSeq {
        let icedax_id = driveInfo.discid

        match icedax_id with
        | Some id -> id
        | None -> ()

        try
            let! abcde_id = Abcde.getMusicBrainzDiscIdAsync driveNumber

            match abcde_id with
            | Some id -> id
            | None -> ()
        with ex ->
            Console.Error.WriteLine(ex)
    }

    let private queryMusicBrainzAsync discId = task {
        try
            return! MusicBrainz.getInfoAsync discId
        with ex ->
            Console.Error.WriteLine(ex)
            return None
    }

    let getDiscInfoAsync (driveNumber: int) = task {
        printfn $"[Discovery] [{driveNumber}] Scanning drive {driveNumber}..."

        let! driveInfo = Icedax.getInfoAsync driveNumber
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
                getDiscIdsAsync driveNumber driveInfo
                |> TaskSeq.chooseAsync queryMusicBrainzAsync
                |> TaskSeq.tryHead

            match candidate with
            | Some newDisc ->
                printfn $"[Discovery] [{driveNumber}] Using title {newDisc.title} from MusicBrainz"
                return { driveInfo with disc = newDisc }
            | None ->
                printfn $"[Discovery] [{driveNumber}] Not found on MusicBrainz"
                printfn $"[Discovery] [{driveNumber}] Using title {disc.title} from icedax"
                return driveInfo
    }

    let getAllDiscInfoAsync (driveNumbers: int seq) =
        driveNumbers
        |> Seq.map getDiscInfoAsync
        |> Task.WhenAll
