namespace SatRadioProxy.SiriusXM

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Json

open SatRadioProxy

// Based on Python scripts from:
// - https://github.com/andrew0/SiriusXM
// - https://github.com/PaulWebster/SiriusXM/tree/PaulWebster-cookies

exception CannotAuthenticateException
exception LoginFailedException
exception RecievedErrorException of code: int * message: string

module SiriusXMClient =
    type Credentials = {
        username: string
        password: string
        region: string
    }

    let USER_AGENT = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_12_6) AppleWebKit/604.5.6 (KHTML, like Gecko) Version/11.0.3 Safari/604.5.6"
    let REST_BASE = "https://player.siriusxm.com/rest/v2/experience/modules/"
    let LIVE_PRIMARY_HLS = "https://siriusxm-priprodlive.akamaized.net"

    let mutable key = None

    let cookies = new CookieContainer()

    let mutable private currentCredentials = None

    let setCredentials creds =
        currentCredentials <- Some creds
        for cookie in cookies.GetAllCookies() do
            cookie.Expired <- true

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

    let isSessionAuthenticated () =
        getCookie "AWSALB" <> None && getCookie "JSESSIONID" <> None

    let loginAsync cancellationToken = task {
        match currentCredentials with
        | None ->
            return false
        | Some credentials ->
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
                                appRegion = credentials.region
                                deviceModel = "K2WebClient"
                                clientDeviceId = "null"
                                player = "html5"
                                clientDeviceType = "web"
                            |}
                            standardAuth = {|
                                username = credentials.username
                                password = credentials.password
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
        match currentCredentials with
        | None ->
            return false
        | Some credentials ->
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
                                appRegion = credentials.region
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

            return data.ModuleListResponse.status = 1 && isSessionAuthenticated ()
    }

    let confirmAuthentication cancellationToken = task {
        let! ok = task {
            if (isSessionAuthenticated ()) then return true
            else return! authenticateAsync cancellationToken
        }

        if not ok then
            raise CannotAuthenticateException
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

    let rec getPlaylistUrl (guid: Guid) channelId cancellationToken = task {
        do! confirmAuthentication cancellationToken

        let executeAsync () = task {
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

            use! res = client.GetAsync(
                sprintf "tune/now-playing-live?%s" queryString,
                cancellationToken)
            let! string = res.EnsureSuccessStatusCode().Content.ReadAsStringAsync(cancellationToken)
            File.WriteAllText("""C:\Users\isaac\Desktop\json2.json""", string)
            return string |> Utility.deserializeAs {|
                ModuleListResponse = {|
                    messages = [{|
                        message = ""
                        code = 0
                    |}]
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
                                    //markerLists = [{|
                                    //    layer = ""
                                    //    markers = [{|
                                    //        cut = {|
                                    //            title = ""
                                    //            artists = [{|
                                    //                name = ""
                                    //            |}]
                                    //            album = {|
                                    //                title = ""
                                    //            |}
                                    //            cutContentType = ""
                                    //        |}
                                    //    |}]
                                    //|}]
                                |}
                            |}
                        |}]
                    |}
                |}
            |}
        }

        let! data = task {
            let! result = executeAsync ()

            let message = result.ModuleListResponse.messages.Head.message
            let messageCode = result.ModuleListResponse.messages.Head.code

            match messageCode with
            | 100 ->
                return result
            | 201 | 208 ->
                let! authenticated = authenticateAsync cancellationToken

                if not authenticated then
                    raise LoginFailedException

                return! executeAsync ()
            | _ ->
                return raise (RecievedErrorException (messageCode, message))
        }

        let liveChannelData = data.ModuleListResponse.moduleList.modules[0].moduleResponse.liveChannelData

        if Option.isNone key then
            for info in liveChannelData.customAudioInfos do
                for chunk in info.chunks.chunks do
                    key <- Some (Convert.FromBase64String chunk.key)

        //let cut =
        //    liveChannelData.markerLists
        //    |> Seq.where (fun m -> m.layer = "cut")
        //    |> Seq.collect (fun m -> m.markers)
        //    |> Seq.map (fun m -> m.cut)
        //    |> Seq.tryLast

        //match cut with
        //| Some c -> sprintf "%s - %s" c.title (String.concat ", " [for a in c.artists do a.name]) |> System.Diagnostics.Debug.WriteLine
        //| None -> ()

        let url =
            liveChannelData.hlsAudioInfos
            |> Seq.where (fun p -> p.name = "primary")
            |> Seq.sortByDescending (fun p -> [
                p.size = "LARGE"
                p.size = "MEDIUM"
                p.size = "SMALL"
            ])
            |> Seq.map (fun p -> p.url)
            |> Seq.head
        return url.Replace("%Live_Primary_HLS%", LIVE_PRIMARY_HLS)
    }

    let getChannels cancellationToken = task {
        do! confirmAuthentication cancellationToken

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

        use! res = client.PostAsync(
            "get",
            content,
            cancellationToken)
        let! string = res.EnsureSuccessStatusCode().Content.ReadAsStringAsync(cancellationToken)

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
                                        siriusChannelNumber = ""
                                    |}]
                                |}
                            |}
                        |}
                    |}]
                |}
            |}
        |}

        return data.ModuleListResponse.moduleList.modules[0].moduleResponse.contentData.channelListing.channels
    }

    let getFile path cancellationToken = task {
        do! confirmAuthentication cancellationToken

        let parameters = [
            "token", getSxmAkToken ()
            "consumer", "k2"
            "gupId", getGupId ()
        ]

        let queryString = String.concat "&" [
            for key, value in parameters do
                $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}"
        ]

        let baseUri = new Uri(LIVE_PRIMARY_HLS)
        let uri = new Uri(
            baseUri,
            sprintf "%s?%s" path queryString)

        use! res = client.GetAsync(
            uri,
            cancellationToken)
        use! stream = res.EnsureSuccessStatusCode().Content.ReadAsStreamAsync(cancellationToken)

        use ms = new MemoryStream()
        do! stream.CopyToAsync(ms)
        let data = ms.ToArray()

        return {|
            content = data
            contentType = res.Content.Headers.ContentType.MediaType
        |}
    }
