namespace RadioHomeEngine

open System
open System.Diagnostics
open System.Threading.Tasks

module Abcde =
    let mutable ripping = false

    let getMusicBrainzDiscIdAsync (device: string) = task {
        let proc =
            new ProcessStartInfo(
                "abcde-musicbrainz-tool",
                $"--command id --device {device}",
                RedirectStandardOutput = true)
            |> Process.Start

        let readTask = task {
            use sr = proc.StandardOutput
            let! output = sr.ReadToEndAsync()
            return output.Split(' ') |> Array.head
        }

        let! _ = Task.WhenAny(
            proc.WaitForExitAsync(),
            Task.Delay(TimeSpan.FromSeconds(15)))

        if not proc.HasExited then proc.Kill()

        let! id = readTask

        if String.IsNullOrEmpty(id)
        then return None
        else return Some id
    }

    let ripAsync scope = task {
        if ripping then
            Console.Error.WriteLine("Rip in progress; not starting new rip")
        else
            ripping <- true

            try
                let! dirs = LyrionCLI.General.getMediaDirsAsync()

                let dir =
                    dirs
                    |> Seq.tryHead
                    |> Option.defaultWith (fun () -> failwith "No media_dir found to rip to")

                for device in DiscDrives.getDevices scope do
                    let proc =
                        new ProcessStartInfo(
                            "abcde",
                            $"-a move,embedalbumart,clean -d {device} -o flac -f -N",
                            WorkingDirectory = dir)
                        |> Process.Start

                    do! proc.WaitForExitAsync()

                    do! DiscDrives.ejectDeviceAsync device

                do! LyrionCLI.General.rescanAsync()
            with ex ->
                Console.Error.WriteLine(ex)

            ripping <- false
    }

    let beginRipAsync scope =
        ignore (ripAsync scope)
