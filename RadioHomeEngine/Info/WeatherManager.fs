namespace RadioHomeEngine

open System
open System.Threading.Tasks
open Microsoft.Extensions.Hosting

open LyrionCLI

module WeatherManager =
    type Service() =
        inherit BackgroundService()

        override _.ExecuteAsync cancellationToken = task {
            while not cancellationToken.IsCancellationRequested do
                try
                    let mutable players = []
                    for player in LyrionKnownPlayers.known do
                        let! state = Players.getPowerAsync player
                        if state then
                            players <- player :: players

                    if not (List.isEmpty players) then
                        let! alerts = Weather.getNewAlertsAsync cancellationToken
                        if not (List.isEmpty alerts) then
                            for player in players do
                                do! Speech.readAsync player [for alert in alerts do alert.info]
                with ex -> Console.Error.WriteLine(ex)

                do! Task.Delay(TimeSpan.FromMinutes(5), cancellationToken)
        }
