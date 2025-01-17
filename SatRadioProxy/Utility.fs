namespace SatRadioProxy

open System
open System.IO

module Utility =
    let (|Int32|_|) (str: string) =
        match Int32.TryParse(str) with
        | true, value -> Some value
        | false, _ -> None

    let readFile path =
        if File.Exists(path)
        then Some (File.ReadAllText(path))
        else None

    let split (char: char) (str: string) =
        str.Split(
            char,
            StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
