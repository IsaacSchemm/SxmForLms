namespace SxmForLms

open System
open System.Threading.Tasks

open Microsoft.Extensions.Hosting

open FSharp.Data

open SxmForLms.Lyrion
open SxmForLms.SiriusXM
open System.Diagnostics

type NetworkInterfaces = JsonProvider<"""[{"ifname":"lo","addr_info":[{"family":"inet","local":"127.0.0.1"}]}]""">

module SiriusXMFavorites =
    let getAddressAsync cancellationToken = task {
        try
            let proc =
                new ProcessStartInfo("ip", "-j address", RedirectStandardOutput = true)
                |> Process.Start

            let! result = proc.StandardOutput.ReadToEndAsync(cancellationToken)

            let ip =
                result
                |> NetworkInterfaces.Parse
                |> Seq.where (fun i -> i.Ifname <> "lo")
                |> Seq.collect (fun i -> i.AddrInfo)
                |> Seq.where (fun a -> a.Family = "inet")
                |> Seq.map (fun a -> a.Local)
                |> Seq.tryHead
                |> Option.defaultValue "localhost"

            return ip
        with ex ->
            Console.Error.WriteLine(ex)
            return "localhost"
    }

    let runAsync cancellationToken = task {
        let! address = getAddressAsync cancellationToken

        let! channels = SiriusXMClient.getChannelsAsync cancellationToken

        LyrionFavorites.updateFavorites "SiriusXM" [
            for channel in channels do {|
                url = $"http://{address}:{Config.port}/Radio/PlayChannel?num={channel.channelNumber}"
                icon = $"http://{address}:{Config.port}/Radio/ChannelImage?num={channel.channelNumber}"
                text = $"[{channel.channelNumber}] {channel.name}"
            |}
        ]

        do! Task.Delay(TimeSpan.FromHours(12), cancellationToken)
    }

    type Service() =
        inherit BackgroundService()

        override _.ExecuteAsync cancellationToken = task {
            while not cancellationToken.IsCancellationRequested do
                do! runAsync cancellationToken
        }
