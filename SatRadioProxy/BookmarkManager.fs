namespace SatRadioProxy

open System.IO

module BookmarkManager =
    let filename = "bookmarks.txt"

    let getBookmarks () = [
        if File.Exists filename then
            yield! File.ReadAllLines(filename)
    ]

    let setBookmarks ids =
        File.WriteAllLines(filename, ids)
