namespace SatRadioProxy.SiriusXM

open System.Diagnostics
open System.IO

open SatRadioProxy

module SiriusXMPythonScriptManager =
    let private usernameFile = "username.txt"
    let private passwordFile = "password.txt"

    let mutable private currentProcess: Process option = None

    let private getStatus () =
        Utility.readFile usernameFile,
        Utility.readFile passwordFile,
        currentProcess |> Option.filter (fun p -> not p.HasExited)

    let setCredentials (username, password) =
        File.WriteAllText(usernameFile, username)
        File.WriteAllText(passwordFile, password)

    let getChannelsAsync () = task {
        let newProcess =
            match getStatus () with
            | Some username, Some password, None ->
                new ProcessStartInfo(
                    "python3",
                    $"sxm.py -l {username} {password}",
                    RedirectStandardOutput = true)
                |> Process.Start
                |> Option.ofObj
            | _ ->
                None

        let! output = task {
            match newProcess with
            | Some p ->
                let readTask = p.StandardOutput.ReadToEndAsync()
                do! p.WaitForExitAsync()
                return! readTask
            | None ->
                return ""
        }

        return [
            for line in Utility.split '\n' output do
                match Utility.split '|' line with
                | [| id; Utility.Int32 number; name |] ->
                    yield {|
                        id = id
                        number = number
                        name = name
                    |}
                | _ -> ()
        ]
    }

    let start () =
        match getStatus() with
        | Some username, Some password, None ->
            currentProcess <-
                Process.Start(
                    "python3",
                    $"sxm.py -p 8888 {username} {password}")
                |> Option.ofObj
        | _ -> ()

    let stop () =
        currentProcess |> Option.iter (fun p -> p.Kill())
        currentProcess <- None
