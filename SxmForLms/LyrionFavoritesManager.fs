namespace SxmForLms.Lyrion

open System.Diagnostics
open System.IO
open System.Xml

module LyrionFavoritesManager =
    let path = "/var/lib/squeezeboxserver/prefs/favorites.opml"

    let attr (name: string) (node: XmlNode) =
        node.Attributes
        |> Option.ofObj
        |> Option.bind (fun attrs -> Option.ofObj attrs[name])
        |> Option.bind (fun attr -> Option.ofObj attr.Value)

    let getFavorites categoryName = [
        if File.Exists(path) then
            let doc = new XmlDocument()
            doc.Load(path)

            for category in doc.GetElementsByTagName("outline") do
                if attr "text" category = Some categoryName then
                    for favorite in category.ChildNodes do
                        match attr "URL" favorite, attr "text" favorite with
                        | Some url, Some text -> {| url = url; text = text |}
                        | _ -> ()
    ]

    let updateFavorites categoryName desiredFavorites =
        if File.Exists(path) && getFavorites categoryName <> desiredFavorites then
            let doc = new XmlDocument()
            doc.Load(path)

            let oldCategories = [
                for oldCategory in doc.GetElementsByTagName("outline") do
                    oldCategory
            ]

            for oldCategory in oldCategories do
                oldCategory.ParentNode.RemoveChild(oldCategory) |> ignore

            let newCategory = doc.CreateElement("outline")
            newCategory.SetAttribute("icon", "html/images/radio.png")
            newCategory.SetAttribute("text", categoryName)

            for favorite in desiredFavorites do
                let newFavorite = doc.CreateElement("outline")
                newFavorite.SetAttribute("URL", favorite.url)
                newFavorite.SetAttribute("icon", "html/images/radio.png")
                newFavorite.SetAttribute("text", favorite.text)
                newFavorite.SetAttribute("type", "audio")
                newCategory.AppendChild(newFavorite) |> ignore

            let body = doc.GetElementsByTagName("body")[0]
            body.AppendChild(newCategory) |> ignore

            doc.Save(path)

            Process.Start("service", "lyrionmusicserver restart") |> ignore
