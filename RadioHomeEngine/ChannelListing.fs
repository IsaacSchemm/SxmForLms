namespace RadioHomeEngine

module ChannelListing =
    let ListChannelsAsync(cancellationToken) = task {
        let! address = Network.getAddressAsync ()

        let! channels = SiriusXMClient.getChannelsAsync cancellationToken

        let! externalChannels = ExternalStreamSource.ListAsync(cancellationToken)

        return [
            for channel in channels do {|
                category = "SiriusXM"
                url = $"http://{address}:{Config.port}/Radio/PlayChannel?num={channel.channelNumber}"
                icon = $"http://{address}:{Config.port}/Radio/ChannelImage?num={channel.channelNumber}"
                text = $"[{channel.channelNumber}] {channel.name}"
            |}

            {|
                category = "Brown noise"
                url = $"http://{address}:{Config.port}/Noise/playlist.m3u8"
                icon = ""
                text = "Brown noise"
            |}

            for channel in externalChannels do {|
                category = if channel.video then "External (Video)" else "External (Audio)"
                url = $"http://{address}:{Config.port}/Radio/PlayExternalChannel?id={channel.id}"
                icon = ""
                text = channel.name
            |}
        ]
    }
