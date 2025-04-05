﻿namespace SxmForLms

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.RegularExpressions
open System.Threading.Tasks

module Icedax =
    let device = "/dev/cdrom"

    let trackPattern = new Regex("^T([0-9]+): .* title '(.*)")

    let sampleFileSizePattern = new Regex("^samplefile size will be ([0-9]+) bytes")

    let (|Track|_|) (str: string) = Seq.tryHead (seq {
        let m = trackPattern.Match(str)
        if m.Success then
            let trackNumber = int m.Groups[1].Value

            use sr = new StringReader(m.Groups[2].Value)

            let str = String [|
                let mutable finished = false
                while not finished do
                    match sr.Read() with
                    | -1 ->
                        finished <- true
                    | v when char v = '\'' ->
                        finished <- true
                    | v when char v = '\\' ->
                        char (sr.Read())
                    | v ->
                        char v
            |]

            (trackNumber, str)
    })

    let noDiscMessage = "load cdrom please and press enter"

    let rec getInfo () =
        let proc =
            new ProcessStartInfo("icedax", $"-J -g -D {device} -S 1 -L 1 --cddbp-server=gnudb.gnudb.org -v toc", RedirectStandardError = true)
            |> Process.Start

        let body =
            let body = new StringBuilder()

            use sr = proc.StandardError

            let mutable finished = false
            while not finished do
                match sr.Read() with
                | -1 ->
                    finished <- true
                | value ->
                    let c = char value
                    ignore (body.Append(c))

                    printf "%c" c

                    if body.ToString().EndsWith(noDiscMessage) then
                        proc.Kill()

            body.ToString()

        proc.WaitForExit()

        let mutable tracks = []

        for line in Utility.split '\n' (body.ToString()) do
            match line with
            | Track (n, t) ->
                tracks <- {| number = n; title = t |} :: tracks
            | _ -> ()

        {|
            tracks = tracks |> Seq.sortBy (fun t -> t.number) |> Seq.toList
        |}

    let bytesPerSecond = 44100 * sizeof<uint16> * 2
    let sectorsPerSecond = 75
    let bytesPerSector = bytesPerSecond / sectorsPerSecond

    let extractWaveAsync trackNumber skip = task {
        let spanString = $"-t {trackNumber}"

        let factor =
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
            new ProcessStartInfo("icedax", $"-D {device} {spanString} -S 1 -o {factor.sectors} -", RedirectStandardOutput = true, RedirectStandardError = true)
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
            length = length + int64 (factor.sectors * bytesPerSector)
        |}
    }
