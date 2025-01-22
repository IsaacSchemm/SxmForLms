namespace SatRadioProxy.Lyrion

open System.Diagnostics
open System.IO
open System.Xml

open SatRadioProxy
open SatRadioProxy.SiriusXM

module LyrionFavoritesManager =
    type Favorite = {
        url: string
        text: string
    }

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
                        | Some url, Some text -> { url = url; text = text }
                        | _ -> ()
    ]

    let replaceFavorites categoryName channels =
        if File.Exists(path) then
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

            for channel in channels do
                let newFavorite = doc.CreateElement("outline")
                newFavorite.SetAttribute("URL", channel.url)
                newFavorite.SetAttribute("icon", "html/images/radio.png")
                newFavorite.SetAttribute("text", channel.text)
                newFavorite.SetAttribute("type", "audio")
                newCategory.AppendChild(newFavorite) |> ignore

            let body = doc.GetElementsByTagName("body")[0]
            body.AppendChild(newCategory) |> ignore

            doc.Save(path)

    let refreshFavorites () =
        let desiredFavorites = [
            for channel in SiriusXMChannelProvider.channels do {
                url = $"http://{NetworkInterfaceProvider.address}:5000/Home/PlayChannel?num={channel.number}"
                text = $"[{channel.number}] {channel.name}"
            }

            for i in 1 .. 10 do {
                url = $"http://{NetworkInterfaceProvider.address}:5000/Home/PlayBookmark?num={i}"
                text = $"Bookmark #{i} (SatRadioProxy)"
            }
        ]

        if getFavorites "SiriusXM" <> desiredFavorites then
            replaceFavorites "SiriusXM" desiredFavorites
            Process.Start("service", "lyrionmusicserver restart") |> ignore
