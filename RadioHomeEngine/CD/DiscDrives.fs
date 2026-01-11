namespace RadioHomeEngine

open System.Diagnostics
open System.IO

module DiscDrives =
    let all =
        seq { 0 .. 9 }
        |> Seq.map (fun n -> $"/dev/sr{n}")
        |> Seq.where File.Exists
        |> Seq.toList

    let allDriveNumbers =
        seq { 0 .. all.Length - 1 }

    let ejectAsync driveNumber = task {
        use proc = Process.Start("eject", $"-T {all[driveNumber]}")
        do! proc.WaitForExitAsync()
    }
