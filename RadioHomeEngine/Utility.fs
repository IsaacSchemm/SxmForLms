namespace RadioHomeEngine

open System
open System.IO
open System.Text.Json

module Utility =
    let deserializeAs<'T> (_: 'T) (string: string) =
        JsonSerializer.Deserialize<'T>(string)

    let readFile path =
        if File.Exists(path)
        then Some (File.ReadAllText(path))
        else None

    let split (char: char) (str: string) =
        str.Split(
            char,
            StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
