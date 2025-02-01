namespace SxmForLms

open System
open System.Globalization
open System.Threading.Tasks

open Microsoft.Extensions.Hosting

open LyrionCLI

module LyrionIRHandler =
    let (|IRCode|_|) (str: string) =
        match Int32.TryParse(str, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) with
        | true, value -> Some value
        | false, _ -> None

    type Handler(player: Player) =
        let mutable power = false

        let processCommandAsync command = task {
            match command with
            | [x; "power"; "0"] when Player x = player ->
                power <- false
            | [x; "power"; "1"] when Player x = player ->
                power <- true
            | [x; "unknownir"; IRCode ircode; Decimal time] when Player x = player ->
                let mapping = LyrionIR.CustomMappings |> Map.tryFind ircode

                match power, mapping with
                | true, Some (LyrionIR.Simulate newcode) ->
                    do! Players.simulateIRAsync player newcode time
                | true, Some (LyrionIR.Debug message) ->
                    printfn "%s" message
                | false, m ->
                    printfn "Player off, not performing action: %A" m
                | _, None ->
                    printfn "Unknown: %08x" ircode
            | _ -> ()
        }

        let subscriber = reader |> Observable.subscribe (fun command ->
            (processCommandAsync command).GetAwaiter().GetResult())

        let _ = printfn "INIT %A" player

        interface IDisposable with
            member _.Dispose() = subscriber.Dispose()

    type Service() =
        inherit BackgroundService()

        let mutable handlers = Map.empty

        let init player =
            if not (handlers |> Map.containsKey player) then
                let handler = new Handler(player)
                handlers <- handlers |> Map.add player handler

                ignore (Players.getPowerAsync(player))

        override _.ExecuteAsync(cancellationToken) = task {
            use _ = reader |> Observable.subscribe (fun command ->
                match command with
                | [playerid; "client"; "new"]
                | [playerid; "client"; "reconnect"] ->
                    init (Player playerid)
                | _ -> ())

            let! count = Players.countAsync ()
            for i in [0 .. count - 1] do
                let! player = Players.getIdAsync i
                init player

            do! Task.Delay(-1, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)
        }
