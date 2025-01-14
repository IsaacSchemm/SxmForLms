namespace SatRadioProxy

open System
open System.Diagnostics
open System.IO

module SiriusXMPythonScriptManager =
    let private usernameFile = "username.txt"
    let private passwordFile = "password.txt"

    let mutable private currentProcess: Process option = None

    // https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/active-patterns#partial-active-patterns
    let private (|Integer|_|) (str: string) =
        let mutable intvalue = 0
        if Int32.TryParse(str, &intvalue) then Some(intvalue)
        else None

    let private readFile path =
        if File.Exists(path)
        then Some (File.ReadAllText(path))
        else None

    let private getActiveProcess () =
        currentProcess
        |> Option.filter (fun p -> not p.HasExited)

    let private getUsername () =
        readFile usernameFile

    let private getPassword () =
        readFile passwordFile

    let setCredentials (username, password) =
        File.WriteAllText(usernameFile, username)
        File.WriteAllText(passwordFile, password)

    let getChannelsAsync () = task {
        let newProcess =
            match getActiveProcess (), getUsername (), getPassword () with
            | None, Some username, Some password ->
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
            let splitOptions = StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries

            for line in output.Split('\n', splitOptions) do
                match line.Split('|', splitOptions) with
                | [| id; Integer number; name |] ->
                    yield {
                        id = id
                        number = number
                        name = name
                    }
                | _ -> ()
        ]
    }

    let start () =
        match getActiveProcess (), getUsername (), getPassword () with
        | None, Some username, Some password ->
            currentProcess <-
                Process.Start(
                    "python3",
                    $"sxm.py -p 8888 {username} {password}")
                |> Option.ofObj
        | _ -> ()

    let stop () =
        match getActiveProcess () with
        | Some p -> p.Kill()
        | None -> ()

        currentProcess <- None
