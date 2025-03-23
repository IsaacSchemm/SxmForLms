namespace SxmForLms

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Net.Http.Json
open System.Runtime.Caching
open System.Threading
open System.Threading.Tasks

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
