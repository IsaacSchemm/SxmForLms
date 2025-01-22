namespace SatRadioProxy.Streaming

open System
open System.Buffers.Binary
open System.Diagnostics
open System.IO
open System.Net.Http
open System.Security.Cryptography
open System.Threading

open Microsoft.Extensions.Caching.Memory

type Proxy (httpClientFactory: IHttpClientFactory, memoryCache: IMemoryCache) =
    static let keySpace = Guid.NewGuid()

    let store segment =
        let key = (keySpace, segment.path)
        memoryCache.Set(key, segment, TimeSpan.FromMinutes(5))

    let retrieve path =
        let key = (keySpace, path)
        match memoryCache.TryGetValue(key) with
        | true, (:? Segment as segment) -> Some segment
        | _ -> None

    let ffmpeg = "ffmpeg"
    let ipAddress = "localhost"

    member _.GetChunklistAsync (id: string, cancellationToken: CancellationToken) = task {
        use client = httpClientFactory.CreateClient()
        use! resp = client.GetAsync($"http://{ipAddress}:8888/{id}.m3u8", cancellationToken)
        let! text = resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync(cancellationToken)

        let list = ChunklistParser.parse text

        let content =
            list
            |> Seq.skip (list.Length - 5)
            |> Seq.map (fun segment -> store segment)
            |> Seq.map (fun segment -> { store segment with key = "NONE" })
            |> ChunklistParser.write

        return {|
            content = content
            contentType = resp.Content.Headers.ContentType.MediaType
        |}
    }

    member _.GetChunkAsync (path: string, cancellationToken: CancellationToken) = task {
        let segment =
            match retrieve path with
            | Some segment -> segment
            | None -> failwith "Segment path is no longer present in cache"

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

        return {|
            data = segment
            contentType = "video/mp2t"
        |}
    }
