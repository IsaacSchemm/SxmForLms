namespace SxmForLms

open System
open System.IO
open System.Net.Sockets
open System.Text
open System.Threading.Tasks

open Microsoft.Extensions.Hosting

module LyrionCLI =
    let ip = "localhost"

    let mutable private current = None
    let mutable private readTask = Task.CompletedTask
    let mutable private writer = TextWriter.Null

    let encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier = false)

    let recieved = new Event<string list>()

    let startAsync () = task {
        match current with
        | Some _ -> ()
        | None ->
            let client = new TcpClient()
            current <- Some client

            do! client.ConnectAsync(ip, 9090)
            let stream = client.GetStream()

            readTask <- task {
                use sr = new StreamReader(stream, Encoding.UTF8)
                try
                    while current.Value.Connected do
                        let! line = sr.ReadLineAsync()
                        let command =
                            line.Split(' ')
                            |> Seq.map Uri.UnescapeDataString
                            |> Seq.toList
                        recieved.Trigger(command)
                with :? IOException when not current.Value.Connected -> ()
            }

            let sw = new StreamWriter(stream, encoding, AutoFlush = true)

            writer <- sw
    }

    let writeLineAsync (command: string seq) =
        command
        |> Seq.map Uri.EscapeDataString
        |> String.concat " "
        |> writer.WriteLineAsync

    let stopAsync () = task {
        match current with
        | Some client ->
            client.Close()
            writer.Dispose()

            do! readTask

            current <- None
            readTask <- Task.CompletedTask
            writer <- TextWriter.Null
        | None -> ()
    }

    type Service() =
        inherit BackgroundService()

        override _.ExecuteAsync(cancellationToken) = task {
            while not cancellationToken.IsCancellationRequested do
                do! startAsync ()

                try
                    do! Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)
                with :? TaskCanceledException -> ()

                do! writeLineAsync ["00:04:20:1f:8a:9c"; "power"; "1"]

                try
                    do! Task.Delay(TimeSpan.FromSeconds(10), cancellationToken)
                with :? TaskCanceledException -> ()

                do! writeLineAsync ["00:04:20:1f:8a:9c"; "power"; "0"]

                try
                    do! Task.Delay(TimeSpan.FromMinutes(5), cancellationToken)
                with :? TaskCanceledException -> ()

            do! stopAsync ()
        }
