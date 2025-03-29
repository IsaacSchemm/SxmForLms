namespace SxmForLms

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
                    let! count = Players.countAsync ()
                    if count > 0 then
                        let! alerts = Weather.getNewAlertsAsync()
                        if not (List.isEmpty alerts) then
                            for i in [0 .. count - 1] do
                                let! player = Players.getIdAsync i
                                let! powerState = Players.getPowerAsync player
                                if powerState then
                                    do! Reader.readAsync player [
                                        for alert in alerts do
                                            alert.info
                                    ]
                    with ex -> Console.Error.WriteLine(ex)

                do! Task.Delay(TimeSpan.FromMinutes(5), cancellationToken)
        }
