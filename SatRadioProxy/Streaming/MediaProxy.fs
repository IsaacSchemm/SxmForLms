﻿namespace SatRadioProxy.Streaming

open System
open System.Buffers.Binary
open System.Diagnostics
open System.IO
open System.Runtime.Caching
open System.Security.Cryptography
open System.Text
open System.Threading.Tasks

open SatRadioProxy
open SatRadioProxy.SiriusXM

module MediaProxy =
    type Chunklist = {
        channelId: string
        index: int
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

    module MetadataCache =
        let cache = MemoryCache.Default
        let cacheKey = Guid.NewGuid()

        let store key item =
            let _ = cache.Add(
                $"{cacheKey}-{key}",
                item,
                new CacheItemPolicy(AbsoluteExpiration = DateTimeOffset.Now + TimeSpan.FromMinutes(1)))
            ()

        let tryRetrieve key =
            match cache.Get($"{cacheKey}-{key}") with
            | :? 'T as item -> Some item
            | _ -> None

        type RetrievalOptions = {
            key: string
            onRetryAsync: unit -> Task
        }

        let tryRetrieveWithRetryAsync retrieval = task {
            match tryRetrieve retrieval.key with
            | Some item ->
                return item
            | None ->
                let! _ = retrieval.onRetryAsync ()
                return tryRetrieve retrieval.key |> Option.defaultWith (fun () -> raise MediaNotCachedException)
        }

    let getPlaylistAsync id cancellationToken = task {
        let! channels = SiriusXMClient.getChannelsAsync cancellationToken

        let channel =
            channels
            |> Seq.where (fun c -> c.channelId = id)
            |> Seq.head

        let! url = SiriusXMClient.getPlaylistUrlAsync channel.channelGuid channel.channelId cancellationToken
        let! data = SiriusXMClient.getFileAsync (new Uri(url)) cancellationToken

        let baseUri = new Uri(url)

        let lines =
            data.content
            |> Encoding.UTF8.GetString
            |> Utility.split '\n'

        let content = String.concat "\n" [
            let mutable i = 0
            for line in lines do
                if line.StartsWith('#') then
                    line
                else
                    MetadataCache.store $"{id}-{i}" {
                        channelId = id
                        index = i
                        uri = new Uri(baseUri, line)
                    }
                    $"chunklist-{id}-{i}.m3u8"
                    i <- i + 1
        ]

        return content
    }

    let getChunklistAsync id index cancellationToken = task {
        let! (chunklist: Chunklist) = MetadataCache.tryRetrieveWithRetryAsync {
            key = $"{id}-{index}"
            onRetryAsync = fun () -> getPlaylistAsync id cancellationToken
        }

        let! data = SiriusXMClient.getFileAsync chunklist.uri cancellationToken

        let list =
            data.content
            |> Encoding.UTF8.GetString
            |> ChunklistParser.parse

        let content = ChunklistParser.write [
            for segment in list |> List.skip (list.Length - 3) do
                let uri = new Uri(chunklist.uri, segment.path)

                MetadataCache.store $"{chunklist.channelId}-{chunklist.index}-{segment.mediaSequence}" {
                    chunklist = chunklist
                    uri = uri
                    sequenceNumber = segment.mediaSequence
                    encryption =
                        match segment.key with
                        | "METHOD=AES-128,URI=\"key/1\"" -> Key1
                        | "NONE" -> NoEncryption
                        | _ -> raise UnknownEncryptionException
                }

                { segment with key = "NONE"; path = $"chunk-{chunklist.channelId}-{chunklist.index}-{segment.mediaSequence}.ts" }
        ]

        return content
    }

    let getChunkAsync id index sequenceNumber cancellationToken = task {
        let! (chunk: Chunk) = MetadataCache.tryRetrieveWithRetryAsync {
            key = $"{id}-{index}-{sequenceNumber}"
            onRetryAsync = fun () -> getChunklistAsync id index cancellationToken
        }

        let! encryptedData = SiriusXMClient.getFileAsync chunk.uri cancellationToken

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
