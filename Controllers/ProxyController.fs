namespace SatRadioProxy.Controllers

open System
open System.Buffers.Binary
open System.Diagnostics
open System.IO
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Threading

open Microsoft.AspNetCore.Mvc

open SatRadioProxy

type ProxyController (httpClientFactory: IHttpClientFactory) =
    inherit Controller()

    let ffmpeg = "ffmpeg"
    let ipAddress = "localhost"

    [<Route("Proxy/{id}.m3u8")>]
    member this.Chunklist (id: string, cancellationToken: CancellationToken) = task {
        use client = httpClientFactory.CreateClient()
        use! resp = client.GetAsync($"http://{ipAddress}:8888/{id}.m3u8", cancellationToken)
        let! text = resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync(cancellationToken)

        let origList = ChunklistParser.parse text

        let newList =
            origList
            |> List.skip (List.length origList - 5)
            |> List.map (fun segment -> {
                segment with
                    path = $"Segment/{segment.path}?sequence={segment.mediaSequence}"
                    keyTag = None
            })

        let content = ChunklistParser.write newList

        return this.Content(
            content,
            resp.Content.Headers.ContentType.MediaType,
            Encoding.UTF8)
    }

    [<Route("Proxy/Segment/{**path}")>]
    member this.Chunk (path: string, sequence: uint64, cancellationToken: CancellationToken) = task {
        use client = httpClientFactory.CreateClient()

        use algorithm = Aes.Create()
        algorithm.Padding <- PaddingMode.PKCS7
        algorithm.Mode <- CipherMode.CBC
        algorithm.KeySize <- 128
        algorithm.BlockSize <- 128

        let! key = task {
            use! keyResp = client.GetAsync($"http://{ipAddress}:8888/key/1", cancellationToken)
            return! keyResp.EnsureSuccessStatusCode().Content.ReadAsByteArrayAsync(cancellationToken)
        }

        algorithm.Key <- key

        algorithm.IV <-
            let iv = Array.zeroCreate 16
            BinaryPrimitives.WriteUInt128BigEndian(iv.AsSpan(), sequence)
            iv

        let! data = task {
            use! response = client.GetAsync($"http://{ipAddress}:8888/{path}", cancellationToken)
            use! responseStream = response.EnsureSuccessStatusCode().Content.ReadAsStreamAsync(cancellationToken)

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

        let proc = Process.Start(psi)
        if isNull proc then
            failwith "Cannot redirect stdin and stdout for ffmpeg (process variable is null)"

        let! segment = task {
            use buffer = new MemoryStream()

            let writeTask = proc.StandardInput.BaseStream.WriteAsync(data, cancellationToken)
            let readTask = proc.StandardOutput.BaseStream.CopyToAsync(buffer, cancellationToken)

            do! writeTask

            proc.StandardInput.BaseStream.Close()

            do! readTask
            do! proc.WaitForExitAsync(cancellationToken)

            return buffer.ToArray()
        }

        return this.File(segment, "video/mp2t")
    }
