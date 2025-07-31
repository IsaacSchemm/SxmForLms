namespace RadioHomeEngine

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open CrossInterfaceRokuDeviceDiscovery
open RokuDotNet.Client
open RokuDotNet.Client.Input

module Roku =
    type IDevice =
        abstract member MacAddress: string
        abstract member Location: Uri
        abstract member Name: string
        abstract member Input: IRokuDeviceInput

    let private httpClient = lazy (new HttpClient())

    let mutable private pastDevices = []
    let mutable private currentDevices = []

    let UpdateAsync(cancellationToken: CancellationToken) = task {
        let! address = Network.getAddressAsync ()

        if address <> "localhost" then
            pastDevices <- currentDevices
            currentDevices <- []

            let client = new CrossInterfaceRokuDeviceDiscoveryClient([IPAddress.Parse(address)])

            let callback = fun (ctx: DiscoveredDeviceContext) -> task {
                match ctx.Device with
                | :? IHttpRokuDevice as device ->
                    let! info = device.GetDeviceInfoAsync()

                    let obj = {
                        new IDevice with
                            member _.MacAddress = info.WifiMacAddress
                            member _.Location = device.Location
                            member _.Name =
                                if info.UserDeviceName = info.ModelName
                                then device.Location.Host
                                else info.UserDeviceName
                            member _.Input = device.Input
                    }

                    currentDevices <- obj :: currentDevices

                    printfn "Adding Roku device %s [%s]" obj.Name obj.Location.Host
                | _ -> ()

                return false
            }

            try
                do! client.DiscoverDevicesAsync(callback, cancellationToken)
            with :? TaskCanceledException as ex when ex.CancellationToken = cancellationToken -> ()
    }

    let GetDevices() =
        currentDevices @ pastDevices
        |> Seq.distinctBy (fun d -> d.MacAddress)
        |> Seq.sortBy (fun d -> d.Name, d.Location.Host)

    let PlayAsync(device: IDevice, url: string, name: string, cancellationToken: CancellationToken) = task {
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
                use waitTask = Task.Delay(TimeSpan.FromHours(1), cancellationToken)

                use tokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                use updateTask = UpdateAsync(tokenSource.Token)

                try
                    do! waitTask
                with ex ->
                    Console.Error.WriteLine(ex)

                tokenSource.Cancel()

                try
                    do! updateTask
                with ex ->
                    Console.Error.WriteLine(ex)
        }
