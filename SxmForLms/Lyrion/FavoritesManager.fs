namespace SxmForLms

open System
open System.Threading.Tasks

open Microsoft.Extensions.Hosting

module FavoritesManager =
    let runAsync cancellationToken = task {
        let! address = Network.getAddressAsync cancellationToken

        let! channels = SiriusXMClient.getChannelsAsync cancellationToken

        do! LyrionFavorites.updateFavoritesAsync "SiriusXM" [
            for channel in channels do {|
                url = $"http://{address}:{Config.port}/Radio/PlayChannel?num={channel.channelNumber}"
                icon = $"http://{address}:{Config.port}/Radio/ChannelImage?num={channel.channelNumber}"
                text = $"[{channel.channelNumber}] {channel.name}"
            |}
        ]

        do! Task.Delay(TimeSpan.FromHours(12), cancellationToken)
    }

    type Service() =
        inherit BackgroundService()

        override _.ExecuteAsync cancellationToken = task {
            while not cancellationToken.IsCancellationRequested do
                do! runAsync cancellationToken
        }
