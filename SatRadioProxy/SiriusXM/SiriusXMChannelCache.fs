namespace SatRadioProxy.SiriusXM

open System
open System.Runtime.Caching

open SatRadioProxy
open SatRadioProxy.Lyrion

module SiriusXMChannelCache =
    let private cache = MemoryCache.Default
    let private cacheKey = $"{Guid.NewGuid()}"

    let private getOrAddAsync f = task {
        match cache.Get(cacheKey) with
        | :? 'T as item ->
            return item
        | _ ->
            let! item = f ()
            let _ = cache.Add(cacheKey, item, DateTimeOffset.UtcNow.AddHours(1))
            return item
    }

    let getChannelsAsync cancellationToken = getOrAddAsync (fun () -> task {
        let channels =
            SiriusXMClient.getChannels cancellationToken
            |> Async.AwaitTask
            |> Async.RunSynchronously

        LyrionFavoritesManager.updateFavorites "SiriusXM" [
            for channel in channels do {|
                url = $"http://{NetworkInterfaceProvider.address}:{NetworkInterfaceProvider.port}/Home/PlayChannel?num={channel.channelNumber}"
                text = $"[{channel.channelNumber}] {channel.name}"
            |}
        ]

        return channels
    })
