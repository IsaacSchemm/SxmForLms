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
    type IDevice =
        abstract member MacAddress: string
        abstract member Location: Uri
        abstract member Name: string

    let private httpClient = lazy (new HttpClient())

    let mutable Devices = []

    let UpdateAsync(cancellationToken: CancellationToken) = task {
        let timeout =
            let source = new CancellationTokenSource()
            source.CancelAfter(TimeSpan.FromSeconds(30))
            source.Token

        let token =
            let source = CancellationTokenSource.CreateLinkedTokenSource([|
                cancellationToken
                timeout
            |])
            source.Token

        let! address = Network.getAddressAsync ()

        if address <> "localhost" then
            let client = new CrossInterfaceRokuDeviceDiscoveryClient([IPAddress.Parse(address)])

            let mutable devices = []

            let f = fun (ctx: DiscoveredDeviceContext) -> task {
                match ctx.Device with
                | :? IHttpRokuDevice as device ->
                    let! info = device.GetDeviceInfoAsync()

                    let obj = {
                        new IDevice with
                            member _.MacAddress = info.WifiMacAddress
                            member _.Location = device.Location
                            member _.Name = $"{info.UserDeviceName} ({info.ModelName})"
                    }

                    devices <- obj :: devices
                | _ -> ()

                return false
            }

            try
                do! client.DiscoverDevicesAsync(f, token)
            with :? TaskCanceledException as ex when ex.CancellationToken = token -> ()

            Devices <- devices
    }

    let PlayAsync (device: IDevice) (url: string) (name: string) (cancellationToken: CancellationToken) = task {
        let client = httpClient.Value

        use! resp = client.PostAsync(
            new Uri(device.Location, $"/launch/782875?u={Uri.EscapeDataString(url)}&videoName={Uri.EscapeDataString(name)}"),
            null,
            cancellationToken)

        ignore (resp.EnsureSuccessStatusCode())
    }

    type Service() =
        inherit BackgroundService()

        override _.ExecuteAsync cancellationToken = task {
            while not cancellationToken.IsCancellationRequested do
                try
                    do! UpdateAsync(cancellationToken)
                with ex ->
                    Console.Error.WriteLine(ex)
                do! Task.Delay(TimeSpan.FromHours(1), cancellationToken)
        }
