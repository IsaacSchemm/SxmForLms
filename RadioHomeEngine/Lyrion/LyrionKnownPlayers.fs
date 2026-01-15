namespace RadioHomeEngine

open System
open System.Threading.Tasks
open System.Runtime.Caching
open System.Text.RegularExpressions

open Microsoft.Extensions.Hosting

open LyrionCLI

module LyrionKnownPlayers =
    let macAddressRegex = new Regex("^..:..:..:..:..:..$")

    let isMacAddress (str: string) =
        macAddressRegex.IsMatch(str)

    let mutable known = Set.empty

    let mutable onPlayerAdded: (Player -> unit) list = []

    let attachNewPlayerHandler h =
        onPlayerAdded <- h :: onPlayerAdded
        for player in known do
            h player

    let refreshAsync () = task {
        try
            let! count = Players.countAsync ()
            for i in [0 .. count - 1] do
                let! player = Players.getIdAsync i

                if not (known |> Set.contains player) then
                    known <- known |> Set.add player
                    for h in onPlayerAdded do
                        h player
        with ex ->
            Console.Error.WriteLine(ex)
    }

    module PowerStates =
        let cache = MemoryCache.Default

        let keyPrefix = Guid.NewGuid()

        let getKey (Player id) =
            $"{keyPrefix}-{id}"

        let getCachedState player =
            match cache[getKey player] with
            | :? bool as v -> Some v
            | _ -> None

        let setCachedState player state =
            cache.Set(getKey player, state, DateTimeOffset.UtcNow.AddMinutes(16))

        let getCurrentStateAsync player = task {
            try
                let! state = Players.getPowerAsync player
                setCachedState player state
                return state
            with ex ->
                Console.Error.WriteLine(ex)
                printfn "Temporarily assuming power is off for %A" player
                cache.Set(getKey player, false, DateTimeOffset.UtcNow.AddSeconds(30))
                return false
        }

        let getStateAsync player = task {
            match getCachedState player with
            | Some value ->
                return value
            | None ->
                let! state = getCurrentStateAsync player
                return state
        }

    module Names =
        let mutable private names = Map.empty

        let getNameAsync (player: Player) = task {
            printfn "Fetching name of %A" player

            match Map.tryFind player names with
            | Some name ->
                return name
            | None ->
                let! name = LyrionCLI.Players.getNameAsync player
                names <- names |> Map.add player name
                return name
        }

        let getPlayersWithNamesAsync () = task {
            let players = known

            for player in players do
                do! getNameAsync player :> Task

            return [
                for player in players do {|
                    player = player
                    name = Map.tryFind player names |> Option.defaultValue (string player)
                |}
            ]
        }

    type Service() =
        inherit BackgroundService()

        let checkForPowerState command = task {
            try
                match command with
                | x :: _ when isMacAddress x && not (known |> Set.contains (Player x)) ->
                    do! refreshAsync ()
                | x :: "power" :: "0" :: _ ->
                    PowerStates.setCachedState (Player x) false
                | x :: "power" :: "1" :: _ ->
                    PowerStates.setCachedState (Player x) true
                | _ -> ()
            with ex -> Console.Error.WriteLine(ex)
        }

        override _.ExecuteAsync(cancellationToken) = task {
            use _ = reader |> Observable.subscribe (fun command ->
                ignore (checkForPowerState command))

            while not cancellationToken.IsCancellationRequested do
                do! refreshAsync ()

                let waitFor =
                    if Set.isEmpty known
                    then TimeSpan.FromSeconds(5)
                    else TimeSpan.FromMinutes(15)

                do! Task.Delay(waitFor, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)
        }
