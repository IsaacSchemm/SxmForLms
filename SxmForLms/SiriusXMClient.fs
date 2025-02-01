﻿namespace SxmForLms

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Json
open System.Runtime.Caching
open System.Threading
open System.Threading.Tasks

exception LoginFailedException
exception MissingCookieException
exception RecievedErrorException of code: int * message: string

module SiriusXMClient =
    let USER_AGENT = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_12_6) AppleWebKit/604.5.6 (KHTML, like Gecko) Version/11.0.3 Safari/604.5.6"
    let REST_BASE = "https://player.siriusxm.com/rest/v2/experience/modules/"

    let username = File.ReadAllText("username.txt")
    let password = File.ReadAllText("password.txt")
    let region = Config.region

    module Cache =
        let cache = MemoryCache.Default

        let cacheAsync key timeSpan f = task {
            match cache.Get(key) with
            | :? 'T as item ->
                return item
            | _ ->
                let! item = f ()
                let _ = cache.Add(key, item, DateTimeOffset.UtcNow + timeSpan)
                return item
        }

        let configurationAsync = cacheAsync "be5a43d6-b814-4c8c-ac6f-567edd86bbb4" (TimeSpan.FromHours(1))
        let channelsAsync = cacheAsync "35bfe531-3fe6-4b14-93df-9826cfb3d0f2" (TimeSpan.FromHours(1))

    let mutable key = None

    let cookies = new CookieContainer()

    let client =
        let clientHandler = new HttpClientHandler(CookieContainer = cookies)
        let cl = new HttpClient(clientHandler, true, BaseAddress = new Uri(REST_BASE))
        cl.DefaultRequestHeaders.Add("User-Agent", USER_AGENT)
        cl

    let notExpiringSoon (cookie: Cookie) =
        cookie.Expires = DateTime.MinValue
        || cookie.Expires.ToUniversalTime() - TimeSpan.FromMinutes(19) > DateTime.UtcNow

    let getCookie (name: string) =
        cookies.GetAllCookies()
        |> Seq.where (fun c -> c.Name = name)
        |> Seq.where notExpiringSoon
        |> Seq.map (fun c -> c.Value)
        |> Seq.tryHead

    let loginAsync cancellationToken = task {
        let postdata = {|
            moduleList = {|
                modules = [{|
                    moduleRequest = {|
                        resultTemplate = "web"
                        deviceInfo = {|
                            osVersion = "Mac"
                            platform = "Web"
                            sxmAppVersion = "3.1802.10011.0"
                            browser = "Safari"
                            browserVersion = "11.0.3"
                            appRegion = region
                            deviceModel = "K2WebClient"
                            clientDeviceId = "null"
                            player = "html5"
                            clientDeviceType = "web"
                        |}
                        standardAuth = {|
                            username = username
                            password = password
                        |}
                    |}
                |}]
            |}
        |}

        use! resp = client.PostAsync(
            "modify/authentication",
            Json.JsonContent.Create(postdata),
            cancellationToken)

        let! string = resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync(cancellationToken)
        let data = string |> Utility.deserializeAs {|
            ModuleListResponse = {|
                status = 0
            |}
        |}

        return data.ModuleListResponse.status = 1
            && getCookie "SXMDATA" <> None
    }

    let authenticateAsync cancellationToken = task {
        if getCookie "SXMDATA" = None then
            let! loggedIn = loginAsync cancellationToken
            if not loggedIn then
                raise LoginFailedException

        let postdata = {|
            moduleList = {|
                modules = [{|
                    moduleRequest = {|
                        resultTemplate = "web"
                        deviceInfo = {|
                            osVersion = "Mac"
                            platform = "Web"
                            clientDeviceType = "web"
                            sxmAppVersion = "3.1802.10011.0"
                            browser = "Safari"
                            browserVersion = "11.0.3"
                            appRegion = region
                            deviceModel = "K2WebClient"
                            player = "html5"
                            clientDeviceId = "null"
                        |}
                    |}
                |}]
            |}
        |}

        use content = JsonContent.Create(postdata)

        use! res = client.PostAsync(
            "resume?OAtrial=false",
            content,
            cancellationToken)
        let! string = res.EnsureSuccessStatusCode().Content.ReadAsStringAsync(cancellationToken)
        let data = string |> Utility.deserializeAs {|
            ModuleListResponse = {|
                status = 0
            |}
        |}

        return data.ModuleListResponse.status = 1
            && getCookie "AWSALB" <> None
            && getCookie "JSESSIONID" <> None
    }

    let rec getResponseAsync cancellationToken (f: unit -> Task<HttpResponseMessage>) = task {
        use! response = f ()
        let! string = response.EnsureSuccessStatusCode().Content.ReadAsStringAsync(cancellationToken)

        let responseBody = string |> Utility.deserializeAs {|
            ModuleListResponse = {|
                messages = [{|
                    message = ""
                    code = 0
                |}]
            |}
        |}

        let message =
            responseBody.ModuleListResponse.messages
            |> Seq.head

        match message.code with
        | 100 ->
            return string
        | 201 | 208 ->
            let! authenticated = authenticateAsync cancellationToken

            if not authenticated then
                raise LoginFailedException

            return! getResponseAsync cancellationToken f
        | _ ->
            return raise (RecievedErrorException (message.code, message.message))
    }

    let getConfigurationAsync cancellationToken = Cache.configurationAsync (fun () -> task {
        let! string = getResponseAsync cancellationToken (fun () -> task {
            return! client.GetAsync(
                "get/configuration",
                cancellationToken)
        })

        let data = string |> Utility.deserializeAs {|
            ModuleListResponse = {|
                moduleList = {|
                    modules = [{|
                        moduleResponse = {|
                            configuration = {|
                                components = [{|
                                    name = ""
                                    settings = [{|
                                        platform = ""
                                        relativeUrls = [{|
                                            name = ""
                                            url = ""
                                        |}]
                                    |}]
                                |}]
                            |}
                        |}
                    |}]
                |}
            |}
        |}

        return {|
            relativeUrls =
                data.ModuleListResponse.moduleList.modules[0].moduleResponse.configuration.components
                |> Seq.where (fun comp -> comp.name = "relativeUrls")
                |> Seq.collect (fun comp -> comp.settings)
                |> Seq.where (fun setting -> setting.platform = "WEB")
                |> Seq.collect (fun setting -> setting.relativeUrls)
                |> Seq.map (fun rel -> (rel.name, rel.url))
                |> Seq.toList
        |}
    })

    let rec replacePlaceholders (mappings: (string * string) list) (string: string) =
        match mappings with
        | [] -> string
        | (name, value) :: tail -> (replacePlaceholders tail string).Replace($"%%{name}%%", value)

    let getPlaylistAsync (guid: Guid) channelId cancellationToken = task {
        let! configuration = getConfigurationAsync cancellationToken

        let now = DateTimeOffset.UtcNow

        let parameters = [
            "assetGUID", string guid
            "ccRequestType", "AUDIO_VIDEO"
            "channelId", channelId
            "hls_output_mode", "custom"
            "marker_mode", "all_separate_cue_points"
            "result-template", "web"
            "time", string (now.ToUnixTimeMilliseconds())
            "timestamp", (now.ToString("o"))
        ]

        let queryString = String.concat "&" [
            for key, value in parameters do
                $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}"
        ]

        let! string = getResponseAsync cancellationToken (fun () -> task {
            return! client.GetAsync(
                sprintf "tune/now-playing-live?%s" queryString,
                cancellationToken)
        })

        let data = string |> Utility.deserializeAs {|
            ModuleListResponse = {|
                moduleList = {|
                    modules = [{|
                        moduleResponse = {|
                            liveChannelData = {|
                                hlsAudioInfos = [{|
                                    name = ""
                                    size = ""
                                    url = ""
                                |}]
                                customAudioInfos = [{|
                                    name = ""
                                    chunks = {|
                                        chunks = [{|
                                            key = ""
                                        |}]
                                    |}
                                |}]
                                markerLists = [{|
                                    layer = ""
                                    markers = [{|
                                        assetGUID = Guid.Empty
                                        time = 0L
                                        duration = 0.0
                                        cut = {|
                                            title = ""
                                            artists = [{|
                                                name = ""
                                            |}]
                                            album = Some {|
                                                title = ""
                                                creativeArts = Some [{|
                                                    encrypted = false
                                                    size = ""
                                                    ``type`` = ""
                                                    relativeUrl = ""
                                                |}]
                                            |}
                                            cutContentType = ""
                                        |}
                                    |}]
                                |}]
                            |}
                        |}
                    |}]
                |}
            |}
        |}

        let liveChannelData = data.ModuleListResponse.moduleList.modules[0].moduleResponse.liveChannelData

        if Option.isNone key then
            for info in liveChannelData.customAudioInfos do
                for chunk in info.chunks.chunks do
                    key <- Some (Convert.FromBase64String chunk.key)

        return {|
            url =
                liveChannelData.hlsAudioInfos
                |> Seq.where (fun p -> p.name = "primary")
                |> Seq.map (fun p -> p.url)
                |> Seq.head
                |> replacePlaceholders configuration.relativeUrls
            cuts =
                liveChannelData.markerLists
                |> List.where (fun l -> l.layer = "cut")
                |> List.collect (fun l -> l.markers)
                |> List.map (fun m -> {|
                    title = m.cut.title
                    artists = [for a in m.cut.artists do a.name]
                    albums = [
                        for a in Option.toList m.cut.album do {|
                            title = a.title
                            images =
                                a.creativeArts
                                |> Option.defaultValue []
                                |> Seq.where (fun c -> not c.encrypted)
                                |> Seq.where (fun c -> c.``type`` = "IMAGE")
                                |> Seq.sortByDescending (fun c -> [
                                    c.size = "MEDIUM"
                                    c.size = "SMALL"
                                    c.size = "THUMBNAIL"
                                ])
                                |> Seq.map (fun c -> c.relativeUrl)
                                |> Seq.map (replacePlaceholders configuration.relativeUrls)
                                |> Seq.toList
                        |}
                    ]
                    startTime = DateTimeOffset.FromUnixTimeMilliseconds(m.time)
                    endTime =
                        match m.duration with
                        | 0.0 -> None
                        | _ -> Some (DateTimeOffset.FromUnixTimeMilliseconds(m.time) + TimeSpan.FromSeconds(m.duration))
                |})
        |}
    }

    let getChannelsAsync cancellationToken = Cache.channelsAsync (fun () -> task {
        let postdata = {|
            moduleList = {|
                modules = {|
                    moduleArea = "Discovery"
                    moduleType = "ChannelListing"
                    moduleRequest = {|
                        consumeRequests = []
                        resultTemplate = "responsive"
                        alerts = []
                        profileInfos = []
                    |}
                |}
            |}
        |}

        use content = JsonContent.Create(postdata)

        let! string = getResponseAsync cancellationToken (fun () -> task {
            return! client.PostAsync(
                "get",
                content,
                cancellationToken)
        })

        let data = string |> Utility.deserializeAs {|
            ModuleListResponse = {|
                moduleList = {|
                    modules = [{|
                        moduleResponse = {|
                            contentData = {|
                                channelListing = {|
                                    channels = [{|
                                        channelGuid = Guid.Empty
                                        channelId = ""
                                        name = ""
                                        shortName = ""
                                        shortDescription = ""
                                        mediumDescription = ""
                                        url = ""
                                        channelNumber = ""
                                        images = {|
                                            images = [{|
                                                name = ""
                                                url = ""
                                                height = 0
                                                width = 0
                                            |}]
                                        |}
                                    |}]
                                |}
                            |}
                        |}
                    |}]
                |}
            |}
        |}

        let channels =
            data.ModuleListResponse.moduleList.modules[0].moduleResponse.contentData.channelListing.channels
            |> Seq.sortBy (fun channel ->
                match Int32.TryParse(channel.channelNumber) with
                | true, value -> value
                | false, _ -> Int32.MaxValue)

        return channels
    })

    let rec getFileAsync (uri: Uri) (cancellationToken: CancellationToken) = task {
        if getCookie "SXMAKTOKEN" = None || getCookie "SXMDATA" = None then
            let! authenticated = authenticateAsync cancellationToken

            if not authenticated then
                raise LoginFailedException

        let token =
            let split (char: char) (string: string) =
                let index = string.IndexOf(char)
                string.Substring(0, index), string.Substring(index + 1)

            getCookie "SXMAKTOKEN"
            |> Option.get
            |> split '='
            |> snd
            |> split ','
            |> fst

        let sxmData =
            getCookie "SXMDATA"
            |> Option.get
            |> Uri.UnescapeDataString
            |> Utility.deserializeAs {|
                gupId = ""
            |}

        let parameters = [
            "token", token
            "consumer", "k2"
            "gupId", sxmData.gupId
        ]

        let queryString = String.concat "&" [
            for key, value in parameters do
                $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}"
        ]

        use! finalResponse = client.GetAsync(
            $"{uri.GetLeftPart(UriPartial.Path)}?{queryString}",
            cancellationToken)

        use! stream = finalResponse.EnsureSuccessStatusCode().Content.ReadAsStreamAsync(cancellationToken)

        use ms = new MemoryStream()
        do! stream.CopyToAsync(ms)
        let data = ms.ToArray()

        return {|
            content = data
            contentType = finalResponse.Content.Headers.ContentType.MediaType
        |}
    }
