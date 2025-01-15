namespace SatRadioProxy

module SiriusXMChannelProvider =
    let mutable channels = []

    let refreshChannelsAsync () = task {
        SiriusXMPythonScriptManager.stop ()

        let! ch = SiriusXMPythonScriptManager.getChannelsAsync ()
        channels <- ch

        SiriusXMPythonScriptManager.start ()
    }
