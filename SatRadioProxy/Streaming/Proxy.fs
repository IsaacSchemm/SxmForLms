namespace SatRadioProxy.Streaming

open System
open System.Buffers.Binary
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open System.Threading

open Microsoft.Extensions.Caching.Memory

open SatRadioProxy.SiriusXM

type Proxy(memoryCache: IMemoryCache) =
    static let playlist_url_key_space = Guid.NewGuid()
    static let segment_key_space = Guid.NewGuid()

    let store_playlist_url id url =
        let key = (playlist_url_key_space, id)
        memoryCache.Set(key, url, TimeSpan.FromHours(1))

    let retrieve_playlist_url id =
        let key = (playlist_url_key_space, id)
        match memoryCache.TryGetValue(key) with
        | true, (:? string as segment) -> Some segment
        | _ -> None

    let store_segment (uri: Uri) segment =
        let key = (segment_key_space, uri)
        memoryCache.Set(key, segment, TimeSpan.FromMinutes(5))

    let retrieve_segment (uri: Uri) =
        let key = (segment_key_space, uri)
        match memoryCache.TryGetValue(key) with
        | true, (:? Segment as segment) -> Some segment
        | _ -> None

    let ffmpeg = """ffmpeg"""

    let get_playlist_url id = task {
        match retrieve_playlist_url id with
        | Some url ->
            return url
        | None ->
            let! url = SiriusXMClientManager.get_playlist_url id
            ignore (store_playlist_url id url)
            return url
    }

    member _.GetPlaylistAsync(id: string, cancellationToken: CancellationToken) = task {
        let! playlist_url = get_playlist_url id
        let! data = SiriusXMClientManager.get_file playlist_url
        return data
    }

    member _.GetChunklistAsync(id: string, path: string, cancellationToken: CancellationToken) = task {
        let! playlist_url = get_playlist_url id

        let base_uri = new Uri(playlist_url)
        let uri = new Uri(base_uri, path)

        let! data = SiriusXMClientManager.get_file uri.AbsoluteUri

        let list =
            data.content
            |> Encoding.UTF8.GetString
            |> ChunklistParser.parse

        let content =
            list
            |> Seq.skip (list.Length - 5)
            |> Seq.map (fun segment ->
                let segment_uri = new Uri(uri, segment.path)
                store_segment segment_uri segment)
            |> Seq.map (fun segment -> { segment with key = "NONE" })
            |> ChunklistParser.write

        return {|
            content = content
            content_type = data.contentType
        |}
    }

    member _.GetChunkAsync(id: string, path: string, cancellationToken: CancellationToken) = task {
        let! playlist_url = get_playlist_url id

        let base_uri = new Uri(playlist_url)
        let uri = new Uri(base_uri, path)

        let segment =
            match retrieve_segment uri with
            | Some segment -> segment
            | None -> failwith "Segment path is no longer present in cache"

        let! result = SiriusXMClientManager.get_file uri.AbsoluteUri

        let raw = result.content

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

                match SiriusXMClient.key with
                | Some key -> algorithm.Key <- key
                | None -> ()

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
            content_type = "video/mp2t"
        |}
    }
