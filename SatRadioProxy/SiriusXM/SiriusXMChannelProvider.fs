namespace SatRadioProxy.SiriusXM

module SiriusXMChannelProvider =
    type Channel = {
        id: string
        number: int
        name: string
    }

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
        channels <- [
            for channel in ch do {
                id = channel.id
                number = channel.number
                name = channel.name
            }
        ]

        SiriusXMPythonScriptManager.start ()
    }
