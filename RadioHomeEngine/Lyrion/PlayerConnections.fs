namespace RadioHomeEngine

module PlayerConnections =
    let mutable private all: PlayerConnection list = []

    let GetAll() =
        all

    let TryGet(player) =
        all
        |> Seq.where (fun data -> data.Player = player)
        |> Seq.tryHead

    let Add(playerData) =
        all <- playerData :: all

    let IsOn(player) =
        match TryGet(player) with
        | Some p -> p.PowerState
        | None -> false

    let SetPowerState(player, state) =
        match TryGet(player) with
        | Some p -> p.PowerState <- state
        | None -> ()
