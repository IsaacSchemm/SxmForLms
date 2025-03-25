namespace SxmForLms

open System
open System.IO
open System.Net.Http
open System.Runtime.Caching
open System.Threading.Tasks

module NWS =
    let USER_AGENT = "SxmForLms/0.1 (https://github.com/IsaacSchemm/SxmForLms)"
    let REST_BASE = "https://api.weather.gov/"

    let client =
        let cl = new HttpClient(BaseAddress = new Uri(REST_BASE))
        cl.DefaultRequestHeaders.Add("User-Agent", USER_AGENT)
        cl

    let nyc = (40.7m, -74.0m)

    let (latitude, longitude) =
        let filename = "location.txt"
        if filename |> File.Exists then
            match filename |> File.ReadAllText |> Utility.split ',' with
            | [| Decimal lat; Decimal lng |] -> lat, lng
            | _ -> nyc
        else
            nyc

    let cache (ts: TimeSpan) (f: unit -> Task<'T>) =
        let key = $"{Guid.NewGuid()}"
        new Func<Task<'T>>(fun () -> task {
            match MemoryCache.Default[key] with
            | :? 'T as item ->
                return item
            | _ ->
                let! result = f ()
                MemoryCache.Default.Add(key, result, DateTime.UtcNow + ts) |> ignore
                return result
        })

    let getPointAsync = cache (TimeSpan.FromHours(4)) (fun () -> task {
        use! resp = client.GetAsync($"/points/{latitude},{longitude}")
        let! json = resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync()
        return json |> Utility.deserializeAs {|
            properties = {|
                forecast = ""
                relativeLocation = {|
                    properties = {|
                        city = ""
                        state = ""
                        distance = {|
                            unitCode = ""
                            value = 0.0
                        |}
                    |}
                |}
            |}
        |}
    })

    let getForecastAsync = cache (TimeSpan.FromMinutes(15)) (fun () -> task {
        let! point = getPointAsync.Invoke()
        use! resp = client.GetAsync(point.properties.forecast)
        let! json = resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync()
        return json |> Utility.deserializeAs {|
            properties = {|
                generatedAt = DateTimeOffset.MinValue
                periods = [{|
                    name = ""
                    isDaytime = false
                    temperature = 0
                    temperatureUnit = ""
                    shortForecast = ""
                    detailedForecast = ""
                |}]
            |}
        |}
    })

    let getActiveAlertsAsync () = task {
        use! resp =
            if DateTime.UtcNow < new DateTime(2025, 3, 26)
            then client.GetAsync($"/alerts/active?area=WI")
            else client.GetAsync($"/alerts/active?point={latitude},{longitude}")
        let! json = resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync()
        return json |> Utility.deserializeAs {|
            features = [{|
                properties = {|
                    id = ""
                    sent = DateTimeOffset.MinValue
                    expires = DateTimeOffset.MinValue
                    headline = ""
                    description = ""
                    instruction = ""
                |}
            |}]
        |}
    }
