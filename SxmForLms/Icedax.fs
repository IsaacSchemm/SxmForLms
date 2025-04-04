namespace SxmForLms

open System
open System.Diagnostics
open System.Text.RegularExpressions
open System.Threading.Tasks

module Icedax =
    let albumTitlePattern = new Regex("^Album title: '([^']*)'")
    let trackPattern = new Regex("^T([0-9]+): .* title '([^']*)'")
    let isrcPattern = new Regex("^T: +([0-9]+) ISRC: ([^ ]+)")

    let sampleFileSizePattern = new Regex("^samplefile size will be ([0-9]+) bytes")

    let (|AlbumTitle|_|) (str: string) =
        let m = albumTitlePattern.Match(str)
        if m.Success then Some (m.Groups[1].Value) else None

    let (|Track|_|) (str: string) =
        let m = trackPattern.Match(str)
        if m.Success then Some (m.Groups[1].Value, m.Groups[2].Value) else None

    let (|ISRC|_|) (str: string) =
        let m = isrcPattern.Match(str)
        if m.Success then Some (m.Groups[1].Value, m.Groups[2].Value) else None

    let getInfoAsync cancellationToken = task {
        let proc =
            new ProcessStartInfo("icedax", $"-J -g -D /dev/cdrom", RedirectStandardError = true)
            |> Process.Start

        use sr = proc.StandardError
        let readTask = task {
            return! sr.ReadToEndAsync(cancellationToken)
        }

        do! proc.WaitForExitAsync(cancellationToken)
        let! body = readTask

        let mutable title = None
        let mutable trackNumbers = Set.empty
        let mutable trackTitles = Map.empty
        let mutable trackISRCs = Map.empty

        for line in Utility.split '\n' body do
            match line with
            | AlbumTitle t when t <> "" ->
                title <- Some t
            | Track (n, t) ->
                trackNumbers <- trackNumbers |> Set.add n
                if t <> "" then
                    trackTitles <- trackTitles |> Map.add n t
            | ISRC (n, c) ->
                trackISRCs <- trackISRCs |> Map.add n c
            | _ -> ()

        return {|
            title = title
            tracks = [
                for n in trackNumbers |> Seq.sortBy id do {|
                    number = n
                    title = trackTitles |> Map.tryFind n
                    isrc = trackISRCs |> Map.tryFind n
                |}
            ]
        |}
    }

    type Span = Track of int | WholeDisc

    let extractWaveAsync span skip = task {
        let spanString =
            match span with
            | Track n -> $"-t {n}"
            | WholeDisc -> "-d 99999"

        let factor =
            let bytesPerSecond = 44100 * sizeof<uint16> * 2
            let sectorsPerSecond = 75
            let bytesPerSector = bytesPerSecond / sectorsPerSecond

            let mutable bytes = skip
            let mutable sectors = 0

            while bytes > bytesPerSector + 1000 do
                bytes <- bytes - bytesPerSector
                sectors <- sectors + 1

            {|
                sectors = sectors
                bytes = bytes
            |}

        let proc =
            new ProcessStartInfo("icedax", $"-D /dev/cdrom {spanString} -S 1 -o {factor.sectors} -", RedirectStandardOutput = true, RedirectStandardError = true)
            |> Process.Start

        let! length = task {
            let tcs = new TaskCompletionSource<int64>()

            ignore (task {
                try
                    use sr = proc.StandardError
                    let mutable finished = false
                    while not finished do
                        let! line = sr.ReadLineAsync()
                        if isNull line then
                            finished <- true
                        else
                            let m = sampleFileSizePattern.Match(line)
                            if m.Success then
                                tcs.SetResult(Int64.Parse(m.Groups[1].Value))
                with ex ->
                    Console.Error.WriteLine(ex)
            })

            return! tcs.Task
        }

        let stream = proc.StandardOutput.BaseStream

        let buffer = Array.zeroCreate factor.bytes
        do! stream.ReadExactlyAsync(buffer.AsMemory())

        return {|
            stream = stream
            length = length
        |}
    }
