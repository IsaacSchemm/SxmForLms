namespace SxmForLms

open System
open System.Threading.Tasks

open Microsoft.Extensions.Hosting

module FavoritesManager =
    let runAsync cancellationToken = task {
        let! address = Network.getAddressAsync cancellationToken

        let! siriusXMChannels = SiriusXMClient.getChannelsAsync cancellationToken

        do! LyrionFavorites.updateFavoritesAsync "SiriusXM" [
            for channel in siriusXMChannels do {|
                url = $"http://{address}:{Config.port}/Radio/PlayChannel?num={channel.channelNumber}"
                icon = $"http://{address}:{Config.port}/Radio/ChannelImage?num={channel.channelNumber}"
                text = $"[{channel.channelNumber}] {channel.name}"
            |}
        ]

        let! musicChoiceChannels = MusicChoiceClient.getChannelsAsync ()

        do! LyrionFavorites.updateFavoritesAsync "Music Choice" [
            for channel in musicChoiceChannels do {|
                url = $"http://{address}:{Config.port}/MusicChoice/PlayChannel?channelID={channel.ChannelID}"
                icon = $"http://{address}:{Config.port}/MusicChoice/ChannelImage?channelID={channel.ChannelID}"
                text = $"[{channel.Type}] {channel.Name}"
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
