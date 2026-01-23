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

        if Option.isSome (DataCD.getMountPoint device) then
            let! files = DataCD.scanDeviceAsync device |> Async.AwaitTask

            return {
                device = device
                disc = DataDisc {
                    files = files
                }
            }
        else
            let! scanResults = Icedax.getInfoAsync device |> Async.AwaitTask

            let driveInfo = {
                device = device
                disc = AudioDisc scanResults.disc
            }

            let disc = scanResults.disc

            if disc.tracks = [] then
                printfn $"[Discovery] [{device}] No tracks found on disc"

                if scanResults.hasdata then
                    let! files = DataCD.scanDeviceAsync device |> Async.AwaitTask

                    return {
                        device = device
                        disc = DataDisc {
                            files = files
                        }
                    }
                else
                    return driveInfo

            else if disc.titles <> [] then
                printfn $"[Discovery] [{device}] Using title {disc.titles} from icedax"
                return driveInfo

            else
                printfn $"[Discovery] [{device}] Preparing to query MusicBrainz..."

                let! candidate =
                    asyncGetDiscIds device disc
                    |> AsyncSeq.distinctUntilChanged
                    |> AsyncSeq.mapAsync asyncQueryMusicBrainz
                    |> AsyncSeq.choose id
                    |> AsyncSeq.tryFirst

                match candidate with
                | Some newDisc ->
                    printfn $"[Discovery] [{device}] Using title {newDisc.titles} from MusicBrainz"
                    return { driveInfo with disc = AudioDisc newDisc }
                | None ->
                    printfn $"[Discovery] [{device}] Not found on MusicBrainz"
                    printfn $"[Discovery] [{device}] Using title {disc.titles} from icedax"
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
