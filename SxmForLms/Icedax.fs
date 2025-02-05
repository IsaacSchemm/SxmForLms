namespace SxmForLms

open System.Diagnostics

module Icedax =
    type Span = Track of int | WholeDisc

    let extractWave span =
        let spanString =
            match span with
            | Track n -> $"-t {n}"
            | WholeDisc -> "-d 99999"

        let proc =
            new ProcessStartInfo("icedax", $"-D /dev/cdrom {spanString} -", RedirectStandardOutput = true)
            |> Process.Start

        proc.StandardOutput.BaseStream
