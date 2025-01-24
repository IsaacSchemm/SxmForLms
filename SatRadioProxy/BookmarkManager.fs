namespace SatRadioProxy

open System.IO

module BookmarkManager =
    let filename = "bookmarks.txt"

    let get_bookmarks () = [
        if File.Exists filename then
            yield! File.ReadAllLines(filename)
    ]

    let set_bookmarks ids =
        File.WriteAllLines(filename, ids)
