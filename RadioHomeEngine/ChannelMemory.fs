namespace RadioHomeEngine

module ChannelMemory =
    type Channel = SiriusXM of int | Nothing

    let mutable LastPlayed = Nothing

    let GetNowPlayingAsync(cancellationToken) = task {
        match LastPlayed with
        | SiriusXM num ->
            let! channels = SiriusXMClient.getChannelsAsync cancellationToken
            let channel =
                channels
                |> Seq.where (fun c -> c.channelNumber = $"{num}")
                |> Seq.tryHead

            match channel with
            | None -> return [$"Channel {num} not found"]
            | Some c ->
                let! playlist = SiriusXMClient.getPlaylistAsync c.channelGuid c.channelId cancellationToken
                let song =
                    playlist.cuts
                    |> Seq.sortByDescending (fun cut -> cut.startTime)
                    |> Seq.tryHead

                match song with
                | None -> return []
                | Some c ->
                    let artist = String.concat " / " c.artists
                    return [artist; c.title]
        | Nothing -> return []
    }
