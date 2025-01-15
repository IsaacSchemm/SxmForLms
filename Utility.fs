namespace SatRadioProxy

open System
open System.IO

module Utility =
    // https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/active-patterns#partial-active-patterns
    let (|Int32|_|) (str: string) =
        let mutable intvalue = 0
        if Int32.TryParse(str, &intvalue) then Some(intvalue)
        else None

    let readFile path =
        if File.Exists(path)
        then Some (File.ReadAllText(path))
        else None

    let split (char: char) (str: string) =
        str.Split(
            char,
            StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
