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
    type Chunklist = {
        guid: Guid
        uri: Uri
    }

    type Encryption = Key1 | NoEncryption

    type Chunk = {
        chunklist: Chunklist
        uri: Uri
        sequenceNumber: UInt128
        encryption: Encryption
    }

    exception MediaNotCachedException
    exception UnknownEncryptionException

    let cache = MemoryCache.Default

    let storeChunklist (chunklist: Chunklist) =
        let _ = cache.Add(
            $"{chunklist.guid}",
            chunklist,
            new CacheItemPolicy(SlidingExpiration = TimeSpan.FromMinutes(5)))
        chunklist

    let retrieveChunklist guid =
        match cache.Get($"{guid}") with
        | :? Chunklist as item -> item
        | _ -> raise MediaNotCachedException

    let storeChunk (chunk: Chunk) =
        let _ = cache.Add(
            $"{chunk.chunklist.guid}-{chunk.sequenceNumber}",
            chunk,
            new CacheItemPolicy(SlidingExpiration = TimeSpan.FromMinutes(5)))
        chunk

    let retrieveChunk guid sequenceNumber =
        match cache.Get($"{guid}-{sequenceNumber}") with
        | :? Chunk as item -> item
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
                    let chunklist = storeChunklist {
                        guid = Guid.NewGuid()
                        uri = new Uri(baseUri, line)
                    }
                    $"chunklist-{chunklist.guid}.m3u8"
        ]

        return content
    }

    let getChunklistAsync key cancellationToken = task {
        let chunklist = retrieveChunklist key

        let! data = SiriusXMClient.getFile chunklist.uri.AbsoluteUri cancellationToken

        let list =
            data.content
            |> Encoding.UTF8.GetString
            |> ChunklistParser.parse

        let content = ChunklistParser.write [
            for segment in list |> List.skip (list.Length - 3) do
                let uri = new Uri(chunklist.uri, segment.path)

                let chunk = storeChunk {
                    chunklist = chunklist
                    uri = uri
                    sequenceNumber = segment.mediaSequence
                    encryption =
                        match segment.key with
                        | "METHOD=AES-128,URI=\"key/1\"" -> Key1
                        | "NONE" -> NoEncryption
                        | _ -> raise UnknownEncryptionException
                }

                { segment with key = "NONE"; path = $"chunk-{chunk.chunklist.guid}-{chunk.sequenceNumber}.ts" }
        ]

        return content
    }

    let getChunkAsync guid sequenceNumber cancellationToken = task {
        let chunk = retrieveChunk guid sequenceNumber

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
