namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks

module DataCD =
    let extensions = set [
        ".aac"
        ".aif"
        ".aiff"
        ".flac"
        ".mp3"
        ".m4a"
        ".oga"
        ".ogg"
        ".wav"
        ".wma"
    ]

    let splitOptions =
        StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries

    let getMountPoint (device: string) = Seq.tryHead (seq {
        for line in File.ReadAllLines("/proc/mounts") do
            let info = line.Split([| ' '; '\t' |], splitOptions)
            match List.ofArray info with
            | dev :: dir :: _ when dev = device -> dir
            | _ -> ()
    })

    let mountDeviceAsync (device: string) = task {
        let existingMountPoint = getMountPoint device
        match existingMountPoint with
        | Some dir ->
            return Some dir
        | None ->
            use proc = Process.Start("mount", $"\"{device}\"")
            do! proc.WaitForExitAsync()
            return getMountPoint device
    }

    let unmountDeviceAsync (device: string) = task {
        let stopAt = DateTime.UtcNow.AddSeconds(5)
        while Option.isSome (getMountPoint device) && DateTime.UtcNow < stopAt do
            use proc = Process.Start("umount", $"\"{device}\"")
            do! proc.WaitForExitAsync()
            if proc.ExitCode <> 0 then
                printf "umount process quit with exit code %d" proc.ExitCode
                do! Task.Delay(TimeSpan.FromSeconds(0.25))
    }

    let mountAsync scope = task {
        match scope with
        | SingleDrive x ->
            do! mountDeviceAsync x :> Task
        | AllDrives ->
            do!
                DiscDrives.getAll ()
                |> Seq.map mountDeviceAsync
                |> Task.WhenAll
                :> Task
    }

    let unmountAsync scope = task {
        match scope with
        | SingleDrive x ->
            do! unmountDeviceAsync x
        | AllDrives ->
            do!
                DiscDrives.getAll ()
                |> Seq.map unmountDeviceAsync
                |> Task.WhenAll
                :> Task
    }

    let scanDeviceAsync (device: string) = task {
        let! mountPoint = mountDeviceAsync device

        return [
            match mountPoint with
            | None -> ()
            | Some dir ->
                for file in Directory.EnumerateFiles(dir, "*.*", new EnumerationOptions(RecurseSubdirectories = true)) do
                    if Set.contains (Path.GetExtension(file).ToLowerInvariant()) extensions then
                        if file.StartsWith(dir)
                        then file.Substring(dir.Length).TrimStart(Path.DirectorySeparatorChar)
                        else failwith $"{file} does not start with {dir}"
        ]
    }
