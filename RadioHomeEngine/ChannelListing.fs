namespace RadioHomeEngine

open System

module ChannelListing =
    [<RequireQualifiedAccess>]
    type ChannelInfoSource =
    | SiriusXM of channelGuid: Guid * channelId: string
    | External of int
    | None

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
                info = ChannelInfoSource.SiriusXM (channel.channelGuid, channel.channelId)
            |}

            {|
                category = "Brown noise"
                url = $"http://{address}:{Config.port}/Noise/playlist.m3u8"
                icon = ""
                text = "Brown noise"
                info = ChannelInfoSource.None
            |}

            for channel in externalChannels do {|
                category = if channel.video then "Video" else "Audio"
                url = $"http://{address}:{Config.port}/Radio/PlayExternalChannel?id={channel.id}"
                icon = ""
                text = channel.name
                info = ChannelInfoSource.External channel.id
            |}
        ]
    }

    let GetNowPlayingAsync(channelInfoSource, cancellationToken) = task {
        match channelInfoSource with
        | ChannelInfoSource.SiriusXM (channelGuid, channelId) ->
            let! playlist = SiriusXMClient.getPlaylistAsync channelGuid channelId cancellationToken
            let song =
                playlist.cuts
                |> Seq.sortByDescending (fun cut -> cut.startTime)
                |> Seq.tryHead

            return [
                match song with
                | None -> ()
                | Some c ->
                    String.concat " / " c.artists
                    c.title
                    String.concat " / " [for a in c.albums do a.title]
            ]
        | ChannelInfoSource.External id ->
            let! channel = ExternalStreamSource.GetAsync(id, cancellationToken)
            return channel.nowPlaying
        | ChannelInfoSource.None ->
            return []
    }

    let GetNowPlayingByUrlAsync(url, cancellationToken) = task {
        let! channels = ListChannelsAsync(cancellationToken)

        let info =
            channels
            |> Seq.where (fun c -> c.url = url)
            |> Seq.map (fun c -> c.info)
            |> Seq.tryHead

        match info with
        | Some i ->
            return! GetNowPlayingAsync(i, cancellationToken)
        | None ->
            return []
    }
