namespace RadioHomeEngine

open System

module ChannelListing =
    let ListChannelsAsync(cancellationToken) = task {
        let! address = Network.getAddressAsync ()

        let! channels = SiriusXMClient.getChannelsAsync cancellationToken

        return [
            for channel in channels do {|
                category = "SiriusXM"
                url = $"http://{address}:{Config.port}/SXM/PlayChannel?num={channel.channelNumber}"
                icon = $"http://{address}:{Config.port}/SXM/ChannelImage?num={channel.channelNumber}"
                text = $"[{channel.channelNumber}] {channel.name}"
                num = Int32.Parse(channel.channelNumber)
            |}
        ]
    }
