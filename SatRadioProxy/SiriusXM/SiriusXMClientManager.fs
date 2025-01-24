namespace SatRadioProxy.SiriusXM

open System.IO

open SatRadioProxy

module SiriusXMClientManager =
    let private usernameFile = "username.txt"
    let private passwordFile = "password.txt"

    let private read = Utility.readFile

    let mutable channels = []

    let stop () = ()

    let start () =
        let credentials = (read usernameFile, read passwordFile)

        match credentials with
        | Some username, Some password ->
            SiriusXMClient.setCredentials (Some {
                username = username
                password = password
                region = "US"
            })
        | _ -> ()

    let setCredentials (username, password) =
        File.WriteAllText(usernameFile, username)
        File.WriteAllText(passwordFile, password)
        stop ()

    let refresh_channels () = task {
        start ()

        let! new_channels = SiriusXMClient.getChannels ()
        channels <- new_channels
    }

    let get_playlist_url id = task {
        start ()

        let channel =
            channels
            |> Seq.where (fun c -> c.channelId = id)
            |> Seq.head

        return! SiriusXMClient.getPlaylistUrl channel.channelGuid channel.channelId
    }

    let get_file url = task {
        start ()

        return! SiriusXMClient.getFile url
    }
