namespace RadioHomeEngine

open LyrionCLI

type PlayerConnection(player: Player) =
    member _.Player = player

    member _.MacAddress =
        match player with Player id -> id

    member val Name = "" with get, set
    member val PowerState = false with get, set
