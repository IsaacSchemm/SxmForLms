namespace SatRadioProxy.SiriusXM

module SiriusXMChannelCache =
    let mutable channels = []

    let refresh cancellationToken = task {
        let! new_channels = SiriusXMClient.getChannels cancellationToken
        channels <- new_channels
    }
