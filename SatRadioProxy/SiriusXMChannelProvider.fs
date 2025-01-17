namespace SatRadioProxy

module SiriusXMChannelProvider =
    let mutable channels = [
        {
            id = "big80s"
            number = 8
            name = "Loading..."
        }

        {
            id = "9365"
            number = 341
            name = "Loading..."
        }
    ]

    let refreshChannelsAsync () = task {
        SiriusXMPythonScriptManager.stop ()

        let! ch = SiriusXMPythonScriptManager.getChannelsAsync ()
        channels <- ch

        SiriusXMPythonScriptManager.start ()
    }
