namespace SxmForLms

open System.Diagnostics
open System.IO

module SpeechSynthesis =
    let generateWavAsync (text: string) = task {
        let proc =
            new ProcessStartInfo(
                "espeak",
                "--stdin --stdout -v us-mbrola-2 -s 140 -a 150",
                RedirectStandardInput = true,
                RedirectStandardOutput = true)
            |> Process.Start

        let writeTask = task {
            use stream = proc.StandardInput
            do! stream.WriteAsync(text)
        }

        let readTask = task {
            use ms = new MemoryStream()
            use stream = proc.StandardOutput
            do! stream.BaseStream.CopyToAsync(ms)
            return ms.ToArray()
        }

        do! writeTask
        return! readTask
    }
