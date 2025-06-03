namespace RadioHomeEngine

open System
open System.Diagnostics
open System.Runtime.Caching

module ExternalStreamSource =
    module Cache =
        let cache = MemoryCache.Default

        let cacheAsync key timeSpan f = task {
            match cache.Get(key) with
            | :? 'T as item ->
                return item
            | _ ->
                let! item = f ()
                let _ = cache.Add(key, item, DateTimeOffset.UtcNow + timeSpan)
                return item
        }

    let private readAsync (arguments: string) = task {
        let psi = new ProcessStartInfo("RadioHomeEngine.External", arguments)
        psi.RedirectStandardOutput <- true

        use p = Process.Start(psi)

        let readTask = task {
            use sr = p.StandardOutput
            return! sr.ReadToEndAsync()
        }

        do! p.WaitForExitAsync()
        if p.ExitCode <> 0 then
            failwithf "%s quit with exit code %d" psi.FileName p.ExitCode

        return! readTask
    }

    let listAsync () = Cache.cacheAsync "33902af0-8e7f-467a-8291-58881e1b63a7" (TimeSpan.FromMinutes(30)) (fun () -> task {
        try
            let! json = readAsync "-l"

            return json |> Utility.deserializeAs [{|
                id = 0
                name = ""
                image = ""
                video = true
            |}]
        with ex ->
            Console.Error.WriteLine(ex)
            return []
    })

    let getHlsAsync (id: int) = Cache.cacheAsync $"ad41402d-40da-4042-9301-26aab2551e0a-{id}" (TimeSpan.FromMinutes(1)) (fun () -> task {
        let! result = readAsync $"-h {id}"
        return result.Trim()
    })

    let getNowPlayingAsync (id: int) = Cache.cacheAsync $"aed4fe7a-07bd-4f73-834d-26edf20fd083-{id}" (TimeSpan.FromSeconds(10)) (fun () -> task {
        let! result = readAsync $"-n {id}"
        return result.Trim()
    })
