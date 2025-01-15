namespace SatRadioProxy

open System.Diagnostics

module LyrionManager =
    let refreshFavorites () =
        let desiredFavorites = [
            for channel in SiriusXMChannelProvider.channels do {
                url = $"http://{NetworkInterfaceProvider.address}:5000/Home/PlayChannel?num={channel.number}"
                text = $"[{channel.number}] {channel.name}"
            }

            for i in 1 .. 10 do {
                url = $"http://{NetworkInterfaceProvider.address}:5000/Home/PlayBookmark?num={i}"
                text = $"Bookmark #{i} (SatRadioProxy)"
            }
        ]

        if LyrionFavoritesManager.getFavorites "SiriusXM" <> desiredFavorites then
            LyrionFavoritesManager.replaceFavorites "SiriusXM" desiredFavorites
            Process.Start("service", "lyrionmusicserver restart") |> ignore
