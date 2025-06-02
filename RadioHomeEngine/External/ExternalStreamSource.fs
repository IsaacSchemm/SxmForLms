namespace RadioHomeEngine

open System
open System.Net.Http
open System.Threading

module ExternalStreamSource =
    let private client = new HttpClient()

    let Host = "localhost:5005"

    let ListAsync(cancellationToken: CancellationToken) = task {
        try
            use! resp = client.GetAsync($"http://{Host}/channels", cancellationToken)
            let! json = resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync(cancellationToken)
            return json |> Utility.deserializeAs [{|
                id = 0
                name = ""
                video = true
            |}]
        with ex ->
            Console.Error.WriteLine(ex)
            return []
    }

    let GetAsync(channelId: int, cancellationToken: CancellationToken) = task {
        use! resp = client.GetAsync($"http://{Host}/channels/{channelId}", cancellationToken)
        let! json = resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync(cancellationToken)
        return json |> Utility.deserializeAs {|
            url = ""
        |}
    }
