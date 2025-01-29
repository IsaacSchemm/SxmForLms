namespace SxmForLms

open System
open System.IO
open System.Text.Json

module Utility =
    let (|Int32|_|) (str: string) =
        match Int32.TryParse(str) with
        | true, value -> Some value
        | false, _ -> None

    let deserializeAs<'T> (_: 'T) (string: string) =
        JsonSerializer.Deserialize<'T>(string)

    let dispose (obj: IDisposable) =
        obj.Dispose()

    let readFile path =
        if File.Exists(path)
        then Some (File.ReadAllText(path))
        else None

    let split (char: char) (str: string) =
        str.Split(
            char,
            StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
