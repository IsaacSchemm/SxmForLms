namespace RadioHomeEngine

open System.IO
open RadioHomeEngine.TemporaryMountPoints

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

    let scanDeviceAsync (device: string) = task {
        use! mount = TemporaryMountPoint.CreateAsync(device)
        let dir = mount.MountPath

        return [
            for file in Directory.EnumerateFiles(dir, "*.*", new EnumerationOptions(RecurseSubdirectories = true)) do
                if Set.contains (Path.GetExtension(file).ToLowerInvariant()) extensions then
                    if file.StartsWith(dir)
                    then file.Substring(dir.Length).TrimStart(Path.DirectorySeparatorChar)
                    else failwith $"{file} does not start with {dir}"
        ]
    }

    let readFileAsync (device: string) (path: string) =
        DeviceFileStream.CreateAsync(device, path)
