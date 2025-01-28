namespace SatRadioProxy

open System
open System.Threading.Tasks

open Microsoft.Extensions.Hosting

open SatRadioProxy.Lyrion
open SatRadioProxy.SiriusXM

type SiriusXMLyrionFavoritesService() =
    inherit BackgroundService()

    override _.ExecuteAsync cancellationToken = task {
        while not cancellationToken.IsCancellationRequested do
            let! channels = (SiriusXMClient.getChannelsAsync cancellationToken)

            LyrionFavoritesManager.updateFavorites "SiriusXM" [
                for channel in channels do {|
                    url = $"http://{NetworkInterfaceProvider.address}:{NetworkInterfaceProvider.port}/Radio/PlayChannel?num={channel.channelNumber}"
                    text = $"[{channel.channelNumber}] {channel.name}"
                |}
            ]

            do! Task.Delay(TimeSpan.FromMinutes(0.5), cancellationToken)
    }
