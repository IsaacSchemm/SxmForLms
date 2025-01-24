namespace SatRadioProxy.SiriusXM

open System.IO

open SatRadioProxy

module SiriusXMClientManager =
    let private usernameFile = "username.txt"
    let private passwordFile = "password.txt"

    let private read = Utility.readFile

    let mutable private currentProcess: SiriusXMClient option = None

    let mutable channels = []

    let stop () =
        currentProcess |> Option.iter Utility.dispose
        currentProcess <- None

    let start () =
        if Option.isNone currentProcess then
            let credentials = (read usernameFile, read passwordFile)

            currentProcess <-
                match credentials with
                | Some username, Some password ->
                    let client = new SiriusXMClient(username, password, "US")
                    Some client
                | _ ->
                    None

    let setCredentials (username, password) =
        File.WriteAllText(usernameFile, username)
        File.WriteAllText(passwordFile, password)
        stop ()

    let refresh_channels () = task {
        start ()

        let client = Option.get currentProcess
        let! new_channels = client.GetChannels()
        channels <- new_channels
    }

    let get_playlist_url id = task {
        start ()

        let channel =
            channels
            |> Seq.where (fun c -> c.channelId = id)
            |> Seq.head
            
        let client = Option.get currentProcess
        return! client.GetPlaylistUrl(channel.channelGuid, channel.channelId, 3)
    }

    let get_file url = task {
        start ()

        let client = Option.get currentProcess
        return! client.GetFile(url)
    }
