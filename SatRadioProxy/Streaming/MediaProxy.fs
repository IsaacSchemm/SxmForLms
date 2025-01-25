namespace SatRadioProxy.Streaming

open System
open System.Buffers.Binary
open System.Diagnostics
open System.IO
open System.Runtime.Caching
open System.Security.Cryptography
open System.Text

open SatRadioProxy
open SatRadioProxy.SiriusXM

module MediaProxy =
    type Encryption = Key1 | NoEncryption

    type Chunk = {
        uri: Uri
        sequenceNumber: UInt128
        encryption: Encryption
    }

    type CacheItem = Chunklist of Uri | Chunk of Chunk

    exception MediaNotCachedException
    exception UnknownEncryptionException

    let cache = MemoryCache.Default

    let store (value: CacheItem) =
        let key = $"{Guid.NewGuid()}"
        let _ = cache.Add(key, value, new CacheItemPolicy(SlidingExpiration = TimeSpan.FromMinutes(5)))
        key

    let retrieveChunklist (key: Guid) =
        match cache.Get($"{key}") with
        | :? CacheItem as Chunklist uri -> uri
        | _ -> raise MediaNotCachedException

    let retrieveChunk (key: Guid) =
        match cache.Get($"{key}") with
        | :? CacheItem as Chunk chunk -> chunk
        | _ -> raise MediaNotCachedException

    let getPlaylistAsync id cancellationToken = task {
        let! channels = SiriusXMChannelCache.getChannelsAsync cancellationToken

        let channel =
            channels
            |> Seq.where (fun c -> c.channelId = id)
            |> Seq.head

        let! url = SiriusXMClient.getPlaylistUrl channel.channelGuid channel.channelId cancellationToken
        let! data = SiriusXMClient.getFile url cancellationToken

        let baseUri = new Uri(url)

        let lines =
            data.content
            |> Encoding.UTF8.GetString
            |> Utility.split '\n'

        let content = String.concat "\n" [
            for line in lines do
                if line.StartsWith('#') then
                    line
                else
                    let uri = new Uri(baseUri, line)
                    let guid = store (Chunklist uri)
                    $"chunklist-{guid}.m3u8"
        ]

        return content
    }

    let getChunklistAsync key cancellationToken = task {
        let chunklistUri = retrieveChunklist key

        let! data = SiriusXMClient.getFile chunklistUri.AbsoluteUri cancellationToken

        let list =
            data.content
            |> Encoding.UTF8.GetString
            |> ChunklistParser.parse

        let content = ChunklistParser.write [
            for segment in list |> List.skip (list.Length - 5) do
                let uri = new Uri(chunklistUri, segment.path)

                let chunk = {
                    uri = uri
                    sequenceNumber = segment.mediaSequence
                    encryption =
                        match segment.key with
                        | "METHOD=AES-128,URI=\"key/1\"" -> Key1
                        | "NONE" -> NoEncryption
                        | _ -> raise UnknownEncryptionException
                }

                let guid = store (Chunk chunk)

                { segment with key = "NONE"; path = $"chunk-{guid}.ts#${uri.AbsolutePath}" }
        ]

        return content
    }

    let getChunkAsync key cancellationToken = task {
        let chunk = retrieveChunk key

        let! encryptedData = SiriusXMClient.getFile chunk.uri.AbsoluteUri cancellationToken

        let! data = task {
            match chunk.encryption with
            | NoEncryption ->
                return encryptedData.content
            | Key1 ->
                use algorithm = Aes.Create()
                algorithm.Padding <- PaddingMode.PKCS7
                algorithm.Mode <- CipherMode.CBC
                algorithm.KeySize <- 128
                algorithm.BlockSize <- 128

                algorithm.Key <- Option.get SiriusXMClient.key

                algorithm.IV <-
                    let iv = Array.zeroCreate 16
                    BinaryPrimitives.WriteUInt128BigEndian(iv.AsSpan(), chunk.sequenceNumber)
                    iv

                use outputStream = new MemoryStream()

                do! task {
                    use cryptoStream = new CryptoStream(outputStream, algorithm.CreateDecryptor(), CryptoStreamMode.Write)
                    do! cryptoStream.WriteAsync(encryptedData.content)
                }

                return outputStream.ToArray()
        }

        let ffmpeg =
            new ProcessStartInfo(
                "ffmpeg",
                "-i - -f mpegts -c:a copy -",
                RedirectStandardInput = true,
                RedirectStandardOutput = true)
            |> Process.Start

        let! decrypted_data = task {
            use buffer = new MemoryStream()

            let writeTask = ffmpeg.StandardInput.BaseStream.WriteAsync(data)
            let readTask = ffmpeg.StandardOutput.BaseStream.CopyToAsync(buffer)

            do! writeTask

            ffmpeg.StandardInput.BaseStream.Close()

            do! readTask
            do! ffmpeg.WaitForExitAsync()

            return buffer.ToArray()
        }

        return decrypted_data
    }
