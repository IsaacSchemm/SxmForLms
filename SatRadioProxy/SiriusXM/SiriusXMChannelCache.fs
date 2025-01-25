namespace SatRadioProxy.SiriusXM

open System
open System.Threading

open SatRadioProxy
open SatRadioProxy.Lyrion

module SiriusXMChannelCache =
    let mutable channels = []

    let timer = new Timer(
        new TimerCallback(fun _ ->
            try
                let newChannels =
                    SiriusXMClient.getChannels CancellationToken.None
                    |> Async.AwaitTask
                    |> Async.RunSynchronously

                channels <- newChannels

                LyrionFavoritesManager.updateFavorites "SiriusXM" [
                    for channel in channels do {|
                        url = $"http://{NetworkInterfaceProvider.address}:{NetworkInterfaceProvider.port}/Home/PlayChannel?num={channel.channelNumber}"
                        text = $"[{channel.channelNumber}] {channel.name}"
                    |}
                ]
            with ex ->
                Console.Error.WriteLine(ex)),
        (),
        TimeSpan.Zero,
        TimeSpan.FromDays(0.5))
