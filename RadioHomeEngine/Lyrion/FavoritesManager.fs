namespace RadioHomeEngine

open System
open System.Threading.Tasks

open Microsoft.Extensions.Hosting

module FavoritesManager =
    type Service() =
        inherit BackgroundService()

        override _.ExecuteAsync cancellationToken = task {
            while not cancellationToken.IsCancellationRequested do
                let! playersOn = LyrionKnownPlayers.PowerStates.getPlayersWithStateAsync true

                if List.isEmpty playersOn then
                    let! address = Network.getAddressAsync cancellationToken

                    let! channels = SiriusXMClient.getChannelsAsync cancellationToken

                    do! LyrionFavorites.updateFavoritesAsync "SiriusXM" [
                        for channel in channels do {|
                            url = $"http://{address}:{Config.port}/Radio/PlayChannel?num={channel.channelNumber}"
                            icon = $"http://{address}:{Config.port}/Radio/ChannelImage?num={channel.channelNumber}"
                            text = $"[{channel.channelNumber}] {channel.name}"
                        |}
                    ]

                    do! LyrionFavorites.updateFavoritesAsync "Radio Home Engine" [
                        {|
                            url = $"http://{address}:{Config.port}/Noise/playlist.m3u8"
                            icon = $"http://{address}:{Config.port}/Radio/ChannelImage?num=0"
                            text = $"Brown noise"
                        |}
                    ]

                    do! Task.Delay(TimeSpan.FromHours(12), cancellationToken)
                else
                    printfn "Not updating SiriusXM channel list right now because radio is on: %A" playersOn
        }
