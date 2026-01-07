namespace RadioHomeEngine

open System.Diagnostics

module Abcde =
    let device = "/dev/cdrom"

    let ripAsync () = task {
        let! dirs = LyrionCLI.General.getMediaDirsAsync()

        let dir =
            dirs
            |> Seq.tryHead
            |> Option.defaultWith (fun () -> failwith "No media_dir found to rip to")

        let proc =
            new ProcessStartInfo(
                "abcde",
                $"-a move,embedalbumart,clean -d {device} -o flac -N",
                WorkingDirectory = dir)
            |> Process.Start

        do! proc.WaitForExitAsync()

        let eject = Process.Start("eject")
        do! eject.WaitForExitAsync()

        do! LyrionCLI.General.rescanAsync()

        return ()
    }
