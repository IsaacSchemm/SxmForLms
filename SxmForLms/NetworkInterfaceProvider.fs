namespace SxmForLms

open System
open System.Diagnostics

open FSharp.Data

type NetworkInterfaces = JsonProvider<"""[{"ifname":"lo","addr_info":[{"family":"inet","local":"127.0.0.1"}]}]""">

module NetworkInterfaceProvider =
    let address =
        try
            let proc =
                new ProcessStartInfo("ip", "-j address", RedirectStandardOutput = true)
                |> Process.Start

            proc.StandardOutput.ReadToEnd()
            |> NetworkInterfaces.Parse
            |> Seq.where (fun i -> i.Ifname <> "lo")
            |> Seq.collect (fun i -> i.AddrInfo)
            |> Seq.where (fun a -> a.Family = "inet")
            |> Seq.map (fun a -> a.Local)
            |> Seq.tryHead
            |> Option.defaultValue "localhost"
        with ex ->
            Console.Error.WriteLine(ex)
            "localhost"
