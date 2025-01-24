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
    let USER_AGENT = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_12_6) AppleWebKit/604.5.6 (KHTML, like Gecko) Version/11.0.3 Safari/604.5.6"
    let REST_BASE = "https://player.siriusxm.com/rest/v2/experience/modules/"
    let LIVE_PRIMARY_HLS = "https://siriusxm-priprodlive.akamaized.net"

    let KEY: ReadOnlyMemory<byte> = raise (new NotImplementedException())

type SiriusXMClient(
    username: string,
    password: string,
    region: string
) =
    let cookies = new CookieContainer()

    let client =
        let clientHandler = new HttpClientHandler(CookieContainer = cookies)
        let cl = new HttpClient(clientHandler, true, BaseAddress = new Uri(SiriusXMClient.REST_BASE))
        cl.DefaultRequestHeaders.Add("User-Agent", SiriusXMClient.USER_AGENT)
        cl

    let getCookie (name: string) =
        cookies.GetAllCookies()
        |> Seq.where (fun c -> c.Name = name)
        |> Seq.map (fun c -> c.Value)
        |> Seq.tryHead

    let is_logged_in () =
        getCookie "SXMDATA" <> None

    let is_session_authenticated () =
        getCookie "AWSALB" <> None && getCookie "JSESSIONID" <> None

    let login () = task {
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
            Json.JsonContent.Create(postdata))

        let! string = resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync()
        let data = string |> Utility.deserializeAs {|
            ModuleListResponse = {|
                status = 0
            |}
        |}

        return data.ModuleListResponse.status = 1 && is_logged_in()
    }

    let authenticate () = task {
        let! logged_in = task {
            if (is_logged_in ())
            then return true
            else return! login ()
        }

        if not logged_in then
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

        use! res = client.PostAsync("resume?OAtrial=false", content)
        let! string = res.EnsureSuccessStatusCode().Content.ReadAsStringAsync()
        let data = string |> Utility.deserializeAs {|
            ModuleListResponse = {|
                status = 0
            |}
        |}

        return data.ModuleListResponse.status = 1 && is_session_authenticated ()
    }

    let confirm_authentication () = task {
        let! authentication_ok = task {
            if (is_session_authenticated ()) then return true
            else return! authenticate ()
        }

        if not authentication_ok then
            raise CannotAuthenticateException
    }

    let get_sxmak_token () =
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

    let get_gup_id () =
        getCookie "SXMDATA"
        |> Option.map Uri.UnescapeDataString
        |> Option.map (Utility.deserializeAs {|
            gupId = ""
        |})
        |> Option.map (fun data -> data.gupId)
        |> Option.get

    member this.GetPlaylistUrl(guid: Guid, channel_id, max_attempts) = task {
        do! confirm_authentication ()

        let now = DateTimeOffset.UtcNow

        let parameters = [
            "assetGUID", string guid
            "ccRequestType", "AUDIO_VIDEO"
            "channelId", channel_id
            "hls_output_mode", "custom"
            "marker_mode", "all_separate_cue_points"
            "result-template", "web"
            "time", string (now.ToUnixTimeMilliseconds())
            "timestamp", (now.ToString("o"))
        ]

        let query_string = String.concat "&" [
            for key, value in parameters do
                $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}"
        ]

        use! res = client.GetAsync(sprintf "tune/now-playing-live?%s" query_string)
        let! string = res.EnsureSuccessStatusCode().Content.ReadAsStringAsync()
        let data = string |> Utility.deserializeAs {|
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
                                    size = ""
                                    url = ""
                                |}]
                            |}
                        |}
                    |}]
                |}
            |}
        |}

        let message = data.ModuleListResponse.messages.Head.message
        let message_code = data.ModuleListResponse.messages.Head.code

        match message_code with
        | 201 | 208 when max_attempts > 0 ->
            let! authenticated = authenticate ()

            if not authenticated then
                raise LoginFailedException

            return! this.GetPlaylistUrl(guid, channel_id, max_attempts - 1)
        | 100 ->
            let url =
                data.ModuleListResponse.moduleList.modules[0].moduleResponse.liveChannelData.hlsAudioInfos
                |> Seq.where (fun p -> p.size = "LARGE")
                |> Seq.map (fun p -> p.url)
                |> Seq.head
            return url.Replace("%Live_Primary_HLS%", SiriusXMClient.LIVE_PRIMARY_HLS)
        | _ ->
            return raise (RecievedErrorException (message_code, message))
    }

    member _.GetChannels() = task {
        do! confirm_authentication ()

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

        use! res = client.PostAsync("get", content)
        let! string = res.EnsureSuccessStatusCode().Content.ReadAsStringAsync()

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

    member _.GetFile(path: string) = task {
        do! confirm_authentication ()

        let parameters = [
            "token", get_sxmak_token ()
            "consumer", "k2"
            "gupId", get_gup_id ()
        ]

        let query_string = String.concat "&" [
            for key, value in parameters do
                $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}"
        ]

        let base_uri = new Uri(SiriusXMClient.LIVE_PRIMARY_HLS)
        let uri = new Uri(base_uri, sprintf "%s?%s" path query_string)

        use! res = client.GetAsync(uri)
        use! stream = res.EnsureSuccessStatusCode().Content.ReadAsStreamAsync()

        use ms = new MemoryStream()
        do! stream.CopyToAsync(ms)
        let data = ms.ToArray()

        return {|
            content = data
            content_type = res.Content.Headers.ContentType.MediaType
        |}
    }

    interface IDisposable with
        member _.Dispose() =
            client.Dispose()
