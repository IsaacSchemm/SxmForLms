namespace SxmForLms

open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Runtime.Caching

module MusicChoiceClient =
    type MVPD = Spectrum = 134

    type TokenType = Media | NowPlaying | Navigation

    let cache = MemoryCache.Default

    let secrets = Map.ofList [
        Media, File.ReadAllText("mediaServicesSecret.txt")
        NowPlaying, File.ReadAllText("nowPlayingServicesSecret.txt")
        Navigation, File.ReadAllText("navigationServicesSecret.txt")
    ]

    let USER_AGENT = $"{nameof SxmForLms}/0.1"
    let REST_BASE = "https://navigationservices.musicchoice.com/"

    let client =
        let cl = new HttpClient()
        cl.DefaultRequestHeaders.Add("User-Agent", USER_AGENT)
        cl

    let getTokenAsync tokenType = task {
        let cacheKey = $"39bd5d40-e279-45e5-8d2a-a5854b430fb7-{tokenType}"
        match cache[cacheKey] with
        | :? string as str -> return str
        | _ ->
            let host =
                match tokenType with
                | Media -> "mediaservices.musicchoice.com"
                | NowPlaying -> "nowplayingservices.musicchoice.com"
                | Navigation -> "navigationservices.musicchoice.com"
            use req = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/api/token")
            req.Content <- new StringContent(
                String.concat "&" [
                    "grant_type=client_credentials"
                    "client_id=MCMediaApp"
                    $"client_secret={Uri.EscapeDataString(secrets[tokenType])}"
                ],
                MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded"))
            use! resp = client.SendAsync(req)
            let! json = resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync()
            let obj = json |> Utility.deserializeAs {|
                access_token = ""
                token_type = ""
                expires_in = 0.0
            |}
            if obj.token_type <> "bearer" then
                failwith $"Unexpected token_type {obj.token_type}"
            let expirationTime = DateTime.UtcNow.AddSeconds(obj.expires_in).AddMinutes(-1)
            cache.Add(cacheKey, obj.access_token, expirationTime) |> ignore
            return obj.access_token
    }

    let getChannelsAsync () = task {
        let! bearerToken = getTokenAsync Navigation
        let qs = String.concat "&" [
            "channelType=all"
            $"mvpd={int MVPD.Spectrum}"
        ]
        use req = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://navigationservices.musicchoice.com/api/channels/hierarchy?{qs}")
        req.Headers.Authorization <- new AuthenticationHeaderValue("Bearer", bearerToken)
        use! resp = client.SendAsync(req)
        let! json = resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync()
        let obj = json |> Utility.deserializeAs {|
            Results = [{|
                Cluster = ""
                Channels = [{|
                    ChannelID = 0
                    Name = ""
                    ``Type`` = ""
                    ImageUrl = ""
                    TvRating = ""
                    ContentId = ""
                    ContentType = ""
                |}]
            |}]
        |}
        return List.distinct [
            for result in obj.Results do
                for channel in result.Channels do
                    if channel.ContentType = "channel" then
                        channel
        ]
    }

    let getContentAsync contentId = task {
        let! bearerToken = getTokenAsync Media
        let qs = String.concat "&" [
            "ContentType=channel"
            $"ContentId={Uri.EscapeDataString(contentId)}"
            "appname=MCMediaApp"
            "appversion=1.9"
        ]
        use req = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://mediaservices.musicchoice.com/api/media/subscriptionstreams/content/?{qs}")
        req.Headers.Authorization <- new AuthenticationHeaderValue("Bearer", bearerToken)
        use! resp = client.SendAsync(req)
        let! json = resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync()
        let obj = json |> Utility.deserializeAs {|
            StreamType = ""
            PrimaryUrl = ""
        |}
        if obj.StreamType <> "HLS" then
            failwith "Stream is not HLS"
        return obj.PrimaryUrl
    }

    let getCurrentSongIdAsync contentId = task {
        let! playlistUri = task {
            let! url = getContentAsync contentId
            return new Uri(url)
        }

        let! playlist = task {
            use req = new HttpRequestMessage(HttpMethod.Get, playlistUri)
            use! resp = client.SendAsync(req)
            return! resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync()
        }

        let chunklistPath =
            playlist.Split("\n")
            |> Seq.where (fun str -> not (str.StartsWith("#")))
            |> Seq.head

        let chunklistUri = new Uri(playlistUri, chunklistPath)

        let! chunklist = task {
            use req = new HttpRequestMessage(HttpMethod.Get, chunklistUri)
            use! resp = client.SendAsync(req)
            return! resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync()
        }

        let regex = new System.Text.RegularExpressions.Regex("^#EXT-X-MC-SEGMENT-MAP:[0-9]+=\"([0-9]+)\"")
        let m = regex.Match(chunklist)
        return m.Groups[1].Value
    }

    let getNowPlayingAsync (channelId: int) (songId: string) = task {
        let! bearerToken = getTokenAsync NowPlaying
        use req = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://nowplayingservices.musicchoice.com/api/NowPlaying/OnScreen/{channelId}/{Uri.EscapeDataString(songId)}")
        req.Headers.Authorization <- new AuthenticationHeaderValue("Bearer", bearerToken)
        use! resp = client.SendAsync(req)
        let! json = resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync()
        return json |> Utility.deserializeAs {|
            Line1 = ""
            Line2 = ""
            Line3 = ""
            SongID = ""
            Facts = [{|
                Header = ""
                Fact = ""
            |}]
        |}
    }
