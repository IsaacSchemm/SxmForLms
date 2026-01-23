namespace RadioHomeEngine

open System
open System.IO
open RadioHomeEngine.TemporaryMountPoints

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

    let scanDeviceAsync (device: string) = task {
        use! mountPoint = EphemeralMountPoint.CreateAsync(device)

        let dir = mountPoint.MountPath

        return [
            for file in Directory.EnumerateFiles(dir, "*.*", new EnumerationOptions(RecurseSubdirectories = true)) do
                if Set.contains (Path.GetExtension(file).ToLowerInvariant()) extensions then
                    if file.StartsWith(dir)
                    then file.Substring(dir.Length).TrimStart(Path.DirectorySeparatorChar)
                    else failwith $"{file} does not start with {dir}"
        ]
    }

    [<Obsolete>]
    let readFileAsync (device: string) (path: string) = task {
        let! mountPoint = EstablishedMountPoints.GetOrCreateAsync(device)

        let dir = mountPoint.MountPath

        return new FileStream(
            Path.Combine(
                dir,
                path),
            FileMode.Open,
            FileAccess.Read)
    }
