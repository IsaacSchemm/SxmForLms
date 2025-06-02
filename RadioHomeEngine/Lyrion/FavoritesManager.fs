namespace RadioHomeEngine

open System
open System.Threading.Tasks

open Microsoft.Extensions.Hosting

module FavoritesManager =
    type Service() =
        inherit BackgroundService()

        override _.ExecuteAsync cancellationToken = task {
            do! Task.Delay(TimeSpan.FromSeconds(15), cancellationToken)

            while not cancellationToken.IsCancellationRequested do
                let! playersOn = LyrionKnownPlayers.PowerStates.getPlayersWithStateAsync true

                if List.isEmpty playersOn then
                    let! channels = ChannelListing.ListChannelsAsync(cancellationToken)

                    for category, group in channels |> Seq.groupBy (fun c -> c.category) do
                        if LyrionFavorites.hasCategory category then
                            do! LyrionFavorites.updateFavoritesAsync category [
                                for item in group do {|
                                    url = item.url
                                    icon = item.icon
                                    text = item.text
                                |}
                            ]

                    do! Task.Delay(TimeSpan.FromHours(12), cancellationToken)
                else
                    printfn "Not updating favorites right now because radio is on: %A" playersOn

                    do! Task.Delay(TimeSpan.FromMinutes(5), cancellationToken)
        }
