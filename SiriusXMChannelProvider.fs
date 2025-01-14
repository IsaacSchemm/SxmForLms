namespace SatRadioProxy

module SiriusXMChannelProvider =
    let mutable channels: SiriusXMChannel list = []

    let refreshChannelsAsync () = task {
        SiriusXMPythonScriptManager.stop ()

        let! ch = SiriusXMPythonScriptManager.getChannelsAsync ()
        channels <- ch

        SiriusXMPythonScriptManager.start ()
    }
