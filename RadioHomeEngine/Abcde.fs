namespace RadioHomeEngine

open System
open System.Diagnostics

module Abcde =
    let device = "/dev/cdrom"

    let ripAsync ((*callbacks: (string -> unit) seq*)) = task {
        let! dirs = LyrionCLI.General.getMediaDirsAsync()

        let dir =
            dirs
            |> Seq.tryHead
            |> Option.defaultWith (fun () -> failwith "No media_dir found to rip to")

        let proc =
            new ProcessStartInfo(
                "abcde",
                $"-a move,embedalbumart,clean -d {device} -o flac -N",
                //RedirectStandardError = true,
                WorkingDirectory = dir)
            |> Process.Start

        //let callbackFeeder = task {
        //    use sr = proc.StandardError

        //    let mutable finished = false
        //    while not finished do
        //        let! line = sr.ReadLineAsync()
        //        if isNull line then
        //            finished <- true
        //        else
        //            Console.Error.WriteLine($"[abcde] {line}")

        //            let messages = [
        //                "Grabbing track"
        //                "Encoding track"
        //                "Tagging track"
        //            ]

        //            let index =
        //                messages
        //                |> Seq.map (line.IndexOf)
        //                |> Seq.max

        //            if index >= 0 then
        //                let message = line.Substring(index)
        //                for f in callbacks do
        //                    f message
        //}

        do! proc.WaitForExitAsync()
        do! callbackFeeder

        let eject = Process.Start("eject")
        do! eject.WaitForExitAsync()

        do! LyrionCLI.General.rescanAsync()

        return ()
    }
