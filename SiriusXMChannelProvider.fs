namespace SatRadioProxy

module SiriusXMChannelProvider =
    let mutable channels = [{
        id = ""
        number = 0
        name = "Loading..."
    }]

    let refreshChannelsAsync () = task {
        SiriusXMPythonScriptManager.stop ()

        let! ch = SiriusXMPythonScriptManager.getChannelsAsync ()
        channels <- ch

        SiriusXMPythonScriptManager.start ()
    }
