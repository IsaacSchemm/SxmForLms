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

open Microsoft.Extensions.Caching.Memory

open SatRadioProxy

type ProxyController (httpClientFactory: IHttpClientFactory, memoryCache: IMemoryCache) =
    inherit Controller()

    let ffmpeg = "ffmpeg"
    let ipAddress = "localhost"

    let keySpace = Guid.NewGuid()

    let store segment =
        let key = (keySpace, segment.path)
        memoryCache.Set(key, segment, TimeSpan.FromMinutes(5))

    let retrieve path =
        let key = (keySpace, path)
        match memoryCache.TryGetValue(key) with
        | true, (:? Segment as segment) -> Some segment
        | _ -> None

    [<Route("Proxy/{id}.m3u8")>]
    member this.Chunklist (id: string, cancellationToken: CancellationToken) = task {
        use client = httpClientFactory.CreateClient()
        use! resp = client.GetAsync($"http://{ipAddress}:8888/{id}.m3u8", cancellationToken)
        let! text = resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync(cancellationToken)

        let fullList = ChunklistParser.parse text

        let truncatedList =
            ChunklistParser.parse text
            |> List.skip (fullList.Length - 5)

        for segment in truncatedList do
            ignore (store segment)

        let content =
            truncatedList
            |> Seq.map (fun segment -> { segment with key = "NONE" })
            |> ChunklistParser.write

        return this.Content(
            content,
            resp.Content.Headers.ContentType.MediaType,
            Encoding.UTF8)
    }

    [<Route("Proxy/{**path}")>]
    member this.Chunk (path: string, cancellationToken: CancellationToken) = task {
        let segment = Option.get (retrieve path)

        use client = httpClientFactory.CreateClient()
        client.BaseAddress <- new Uri($"http://{ipAddress}:8888")

        let! raw = task {
            use! response = client.GetAsync(segment.path, cancellationToken)
            return! response.EnsureSuccessStatusCode().Content.ReadAsByteArrayAsync(cancellationToken)
        }

        let! data = task {
            match segment.key with
            | "NONE" ->
                return raw
            | "METHOD=AES-128,URI=\"key/1\"" ->
                use algorithm = Aes.Create()
                algorithm.Padding <- PaddingMode.PKCS7
                algorithm.Mode <- CipherMode.CBC
                algorithm.KeySize <- 128
                algorithm.BlockSize <- 128

                let! key = task {
                    use! response = client.GetAsync("key/1", cancellationToken)
                    return! response.EnsureSuccessStatusCode().Content.ReadAsByteArrayAsync(cancellationToken)
                }

                algorithm.Key <- key

                algorithm.IV <-
                    let iv = Array.zeroCreate 16
                    BinaryPrimitives.WriteUInt128BigEndian(iv.AsSpan(), segment.mediaSequence)
                    iv

                use outputStream = new MemoryStream()

                do! task {
                    use cryptoStream = new CryptoStream(outputStream, algorithm.CreateDecryptor(), CryptoStreamMode.Write)
                    do! cryptoStream.WriteAsync(raw)
                }

                return outputStream.ToArray()
            | _ ->
                return raise (new NotImplementedException())
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
