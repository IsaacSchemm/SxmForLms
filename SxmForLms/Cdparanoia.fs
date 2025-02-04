namespace SxmForLms

open System.Diagnostics
open System.Threading.Tasks

module Cdparanoia =
    type Track = {
        number: int
        length: int
        ``begin``: int
        copy: string
        pre: string
        ch: int
    }

    let listAudioTracksAsync cancellationToken = task {
        let proc =
            new ProcessStartInfo("cdparanoia", "-sQ", RedirectStandardError = true)
            |> Process.Start

        let mutable tracks = []

        use sr = proc.StandardError

        try
            let mutable finished = false
            while not finished do
                let! line = sr.ReadLineAsync(cancellationToken)
                if isNull line then
                    finished <- true
                else
                    match Utility.split ' ' line with
                    | [| DotSuffix (Int32 number); Int32 length; _; Int32 ``begin``; _; copy; pre; Int32 ch |]  ->
                        tracks <- {
                            number = number
                            length = length
                            ``begin`` = ``begin``
                            copy = copy
                            pre = pre
                            ch = ch
                        } :: tracks
                    | _ -> ()

                do! proc.WaitForExitAsync(cancellationToken)
        with :? TaskCanceledException -> ()

        if cancellationToken.IsCancellationRequested then
            proc.Kill()

        return List.rev tracks
    }
