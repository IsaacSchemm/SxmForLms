namespace SxmForLms

open System
open System.Runtime.Caching

open Reader

module Weather =
    type Alert = {
        id: string
        expires: DateTimeOffset
        info: Readable
    }

    let getForecastsAsync () = task {
        let! obj = NWS.getForecastAsync.Invoke()
        return [
            for f in obj.properties.periods do {
                screen = $"{f.name}: {f.temperature}°{f.temperatureUnit}, {f.shortForecast}"
                speech = storeSpeech $"{f.name}: {f.detailedForecast}"
            }
        ]
    }

    let getAlertsAsync () = task {
        let! obj = NWS.getActiveAlertsAsync ()
        return [
            for f in obj.features do {
                id = f.properties.id
                expires = f.properties.expires
                info = {
                    screen = f.properties.headline
                    speech = storeSpeech (String.concat "\n" [
                        f.properties.headline
                        f.properties.description
                        f.properties.instruction
                    ])
                }
            }
        ]
    }

    type Known = Known

    let recordAlert (alert: Alert) =
        MemoryCache.Default.Set(alert.id, Known, alert.expires)

    let isAlertKnown (alert: Alert) =
        match MemoryCache.Default[alert.id] with
        | :? Known -> true
        | _ -> false

    let getNewAlertsAsync () = task {
        let! alerts = getAlertsAsync ()
        return [
            for a in alerts do
                if not (isAlertKnown a) then
                    recordAlert a
                    yield a
        ]
    }
