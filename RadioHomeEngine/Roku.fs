namespace RadioHomeEngine

open System
open System.Net
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open RokuDotNet.Client
open CrossInterfaceRokuDeviceDiscovery
open System.Net.Http

module Roku =
    type Device = {
        mac: string
        name: string
        host: string
        port: int
    }

    let private httpClient = lazy (new HttpClient())

    let mutable Devices = Set.empty

    let UpdateAsync(cancellationToken: CancellationToken) = task {
        let timeoutToken =
            let source = new CancellationTokenSource()
            source.CancelAfter(TimeSpan.FromSeconds(15))
            source.Token

        let token =
            let source = CancellationTokenSource.CreateLinkedTokenSource [|
                timeoutToken
                cancellationToken
            |]

            source.Token

        let! address = Network.getAddressAsync ()

        let client = new CrossInterfaceRokuDeviceDiscoveryClient([IPAddress.Parse(address)])

        let f = fun (ctx: DiscoveredDeviceContext) -> task {
            match ctx.Device with
            | :? IHttpRokuDevice as device ->
                let! info = device.GetDeviceInfoAsync()

                Devices <- Devices |> Set.add {
                    mac = info.WifiMacAddress
                    name = $"{info.UserDeviceName} ({info.ModelName})"
                    host = device.Location.Host
                    port = device.Location.Port
                }
            | _ -> ()

            return false
        }

        try
            do! client.DiscoverDevicesAsync(f, token)
        with :? TaskCanceledException as ex when ex.CancellationToken = token -> ()
    }

    let PlayAsync (device: Device) (url: string) (name: string) (cancellationToken: CancellationToken) = task {
        let client = httpClient.Value

        use! resp = client.PostAsync(
            $"http://{device.host}:{device.port}/launch/782875?u={Uri.EscapeDataString(url)}&videoName={Uri.EscapeDataString(name)}",
            null,
            cancellationToken)

        ignore (resp.EnsureSuccessStatusCode())
    }

    type Service() =
        inherit BackgroundService()

        override _.ExecuteAsync cancellationToken = task {
            while not cancellationToken.IsCancellationRequested do
                do! UpdateAsync(cancellationToken)
                do! Task.Delay(TimeSpan.FromHours(1), cancellationToken)
        }
