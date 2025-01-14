namespace SatRadioProxy

open System.Diagnostics

open FSharp.Data

type NetworkInterfaces = JsonProvider<"""[{"ifname":"lo","addr_info":[{"family":"inet","local":"127.0.0.1"}]}]""">

module NetworkInterfaceProvider =
    let mutable address = "127.0.0.1"

    let updateAddressAsync () = task {
        let proc =
            new ProcessStartInfo("ip", "-j address", RedirectStandardOutput = true)
            |> Process.Start

        if not (isNull proc) then
            let readTask = proc.StandardOutput.ReadToEndAsync()
            do! proc.WaitForExitAsync()
            let! json = readTask

            address <-
                NetworkInterfaces.Parse json
                |> Seq.where (fun i -> i.Ifname <> "lo")
                |> Seq.collect (fun i -> i.AddrInfo)
                |> Seq.where (fun a -> a.Family = "inet")
                |> Seq.map (fun a -> a.Local)
                |> Seq.tryHead
                |> Option.defaultValue address
    }
