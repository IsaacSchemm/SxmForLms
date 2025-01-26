namespace SatRadioProxy.SiriusXM

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Json
open System.Runtime.Caching
open System.Text
open System.Threading
open System.Threading.Tasks

open SatRadioProxy

// Based on Python scripts from:
// - https://github.com/andrew0/SiriusXM
// - https://github.com/PaulWebster/SiriusXM/tree/PaulWebster-cookies

exception CannotAuthenticateException
exception LoginFailedException
exception RecievedErrorException of code: int * message: string

module SiriusXMClient =
    let USER_AGENT = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_12_6) AppleWebKit/604.5.6 (KHTML, like Gecko) Version/11.0.3 Safari/604.5.6"
    let REST_BASE = "https://player.siriusxm.com/rest/v2/experience/modules/"

    let username = File.ReadAllText("username.txt")
    let password = File.ReadAllText("password.txt")
    let region = "US"

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

    let getCookie (name: string) =
        cookies.GetAllCookies()
        |> Seq.where (fun c -> c.Name = name)
        |> Seq.map (fun c -> c.Value)
        |> Seq.tryHead

    let isLoggedIn () =
        getCookie "SXMDATA" <> None

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

        return data.ModuleListResponse.status = 1 && isLoggedIn ()
    }

    let authenticateAsync cancellationToken = task {
        let! loggedIn = task {
            if (isLoggedIn ())
            then return true
            else return! loginAsync cancellationToken
        }

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

    let getSxmAkToken () =
        let split (char: char) (string: string) =
            match string.IndexOf(char) with
            | -1 -> None
            | index -> Some (string.Substring(0, index), string.Substring(index + 1))

        getCookie "SXMAKTOKEN"
        |> Option.bind (split '=')
        |> Option.map snd
        |> Option.bind (split ',')
        |> Option.map fst
        |> Option.get

    let getGupId () =
        getCookie "SXMDATA"
        |> Option.map Uri.UnescapeDataString
        |> Option.map (Utility.deserializeAs {|
            gupId = ""
        |})
        |> Option.map (fun data -> data.gupId)
        |> Option.get

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
                |> Seq.map (fun rel -> rel.name, rel.url)
                |> Seq.toList
        |}
    })

    let rec getPlaylistUrlAsync (guid: Guid) channelId cancellationToken = task {
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

        let url =
            liveChannelData.hlsAudioInfos
            |> Seq.where (fun p -> p.name = "primary")
            |> Seq.map (fun p -> p.url)
            |> Seq.head

        let! configuration = getConfigurationAsync cancellationToken

        let stringBuilder = new StringBuilder(url)
        for name, value in configuration.relativeUrls do
            stringBuilder.Replace($"%%{name}%%", value) |> ignore

        return stringBuilder.ToString()
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

        return data.ModuleListResponse.moduleList.modules[0].moduleResponse.contentData.channelListing.channels
    })

    let rec getFileAsync (uri: Uri) (cancellationToken: CancellationToken) = task {
        let executeAsync () = task {
            let parameters = [
                "token", getSxmAkToken ()
                "consumer", "k2"
                "gupId", getGupId ()
            ]

            let queryString = String.concat "&" [
                for key, value in parameters do
                    $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}"
            ]

            return! client.GetAsync(
                $"{uri.GetLeftPart(UriPartial.Path)}?{queryString}",
                cancellationToken)
        }

        use! initialResponse = executeAsync ()

        use! finalResponse = task {
            if initialResponse.StatusCode = HttpStatusCode.Forbidden then
                let! authenticated = authenticateAsync cancellationToken

                if not authenticated then
                    raise LoginFailedException

                return! executeAsync ()
            else
                return initialResponse.EnsureSuccessStatusCode()
        }

        use! stream = finalResponse.EnsureSuccessStatusCode().Content.ReadAsStreamAsync(cancellationToken)

        use ms = new MemoryStream()
        do! stream.CopyToAsync(ms)
        let data = ms.ToArray()

        return {|
            content = data
            contentType = finalResponse.Content.Headers.ContentType.MediaType
        |}
    }
