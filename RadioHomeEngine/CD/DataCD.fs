namespace RadioHomeEngine

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks

module DataCD =
    let extensions = set [
        ".mp3"
        ".wma"
        ".ogg"
        ".oga"
        ".flac"
        ".wav"
        ".aif"
        ".aiff"
    ]

    type TemporaryMount(device: string) =
        let mountTask = lazy task {
            let dir = Path.Combine([|
                Path.GetTempPath()
                $"{Guid.NewGuid()}"
            |])

            printfn $"[{device}] mounting to {dir}"

            let _ = Directory.CreateDirectory(dir)

            let mount = Process.Start("mount", $"-o ro {device} {dir}")
            do! mount.WaitForExitAsync()

            return dir
        }

        member _.GetMountPointAsync() = mountTask.Value

        interface IAsyncDisposable with
            member _.DisposeAsync() = ValueTask(task {
                let! mountPoint = mountTask.Value

                printfn $"[{device}] unmounting from {mountPoint}"

                let umount = Process.Start("umount", $"{mountPoint}")
                do! umount.WaitForExitAsync()

                Directory.Delete(mountPoint, recursive = false)
            })

    let scanDeviceAsync (device: string) = task {
        use mount = new TemporaryMount(device)
        let! dir = mount.GetMountPointAsync()

        return [
            for file in Directory.EnumerateFiles(dir, "*.*", new EnumerationOptions(RecurseSubdirectories = true)) do
                if Set.contains (Path.GetExtension(file).ToLowerInvariant()) extensions then
                    if file.StartsWith(dir)
                    then file.Substring(dir.Length).TrimStart(Path.DirectorySeparatorChar)
                    else failwith $"{file} does not start with {dir}"
        ]
    }

    let readFileAsync (device: string) (path: string) = task {
        let mount = new TemporaryMount(device)
        let! dir = mount.GetMountPointAsync()

        let fullPath = Path.Combine(dir, path)
        if not (fullPath.StartsWith(dir)) then
            failwith $"{fullPath} is not inside {dir}"

        let stream = File.Open(fullPath, FileMode.Open, FileAccess.Read)

        ignore (task {
            try
                use _ = mount
                while stream.CanRead do
                    do! Task.Delay(5000)
            with ex -> Console.Error.WriteLine(ex)
        })

        return stream
    }
