namespace RadioHomeEngine

open System.Diagnostics
open System.IO

module DiscDrives =
    let all =
        seq { 0 .. 9 }
        |> Seq.map (fun n -> $"/dev/sr{n}")
        |> Seq.where File.Exists
        |> Seq.toList

    let allDriveNumbers = [0 .. all.Length - 1]

    let getDriveNumbers scope =
        match scope with
        | SingleDrive x -> [x]
        | AllDrives -> allDriveNumbers

    let getDevices scope =
        scope
        |> getDriveNumbers
        |> Seq.map (fun i -> all[i])

    let ejectDeviceAsync (device: string) = task {
        use proc = Process.Start("eject", $"-T {device}")
        do! proc.WaitForExitAsync()
    }

    let ejectAsync scope = task {
        for device in getDevices scope do
            do! ejectDeviceAsync device
    }
