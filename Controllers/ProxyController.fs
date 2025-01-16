namespace SatRadioProxy.Controllers

open System
open System.Buffers.Binary
open System.IO
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Diagnostics

open Microsoft.AspNetCore.Mvc

open M3U8Parser

type ProxyController (httpClientFactory: IHttpClientFactory) =
    inherit Controller()

    let ffmpeg = "ffmpeg"
    let ipAddress = "localhost"

    [<Route("Proxy/{id}.m3u8")>]
    member this.Chunklist (id: string, cancellationToken: CancellationToken) = task {
        use client = httpClientFactory.CreateClient()
        use! resp = client.GetAsync($"http://{ipAddress}:8888/{id}.m3u8", cancellationToken)
        let! text = resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync(cancellationToken)

        let playlist = MediaPlaylist.LoadFromText(text)
        let segmentContainer = Seq.exactlyOne playlist.MediaSegments

        segmentContainer.Key <- null

        let mutable segments = List.ofSeq segmentContainer.Segments
        let mutable sequence = playlist.MediaSequence |> Option.ofNullable |> Option.defaultValue 0

        while List.length segments > 5 do
            segments <- List.tail segments
            sequence <- sequence + 1

        playlist.MediaSequence <- sequence

        for segment in segments do
            segment.Uri <- $"{id}-{sequence}.ts?path={Uri.EscapeDataString(segment.Uri)}"
            sequence <- sequence + 1

        segmentContainer.Segments <- ResizeArray segments

        return this.Content(
            string playlist,
            resp.Content.Headers.ContentType.MediaType,
            Encoding.UTF8)
    }

    [<Route("Proxy/{id}-{sequence}.ts")>]
    member this.Chunk (id: string, sequence: uint64, path: string, cancellationToken: CancellationToken) = task {
        use client = httpClientFactory.CreateClient()

        use algorithm = Aes.Create()
        algorithm.Padding <- PaddingMode.PKCS7
        algorithm.Mode <- CipherMode.CBC
        algorithm.KeySize <- 128
        algorithm.BlockSize <- 128

        use! keyResp = client.GetAsync($"http://{ipAddress}:8888/key/1", cancellationToken)
        let! key = keyResp.EnsureSuccessStatusCode().Content.ReadAsByteArrayAsync(cancellationToken)

        let iv = Array.zeroCreate 16
        BinaryPrimitives.WriteUInt128BigEndian(iv.AsSpan(), sequence)

        algorithm.Key <- key
        algorithm.IV <- iv

        use! response = client.GetAsync($"http://{ipAddress}:8888/{path}", cancellationToken)
        use! responseStream = response.EnsureSuccessStatusCode().Content.ReadAsStreamAsync(cancellationToken)

        let! data = task {
            use memoryStream = new MemoryStream()

            do! task {
                use cryptoStream = new CryptoStream(memoryStream, algorithm.CreateDecryptor(), CryptoStreamMode.Write)
                do! responseStream.CopyToAsync(cryptoStream)
            }

            return memoryStream.ToArray()
        }

        let psi = new ProcessStartInfo(
            ffmpeg,
            "-i - -f mpegts -c:a copy -",
            RedirectStandardInput = true,
            RedirectStandardOutput = true)

        match Process.Start(psi) with
        | null ->
            return this.File(data, response.Content.Headers.ContentType.MediaType)
        | proc ->
            use buffer = new MemoryStream()

            let writeTask = proc.StandardInput.BaseStream.WriteAsync(data, cancellationToken)
            let readTask = proc.StandardOutput.BaseStream.CopyToAsync(buffer, cancellationToken)

            do! writeTask

            proc.StandardInput.BaseStream.Close()

            do! readTask
            do! proc.WaitForExitAsync(cancellationToken)

            return this.File(buffer.ToArray(), "video/mp2t")
    }
