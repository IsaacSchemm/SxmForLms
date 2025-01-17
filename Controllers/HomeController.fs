namespace SatRadioProxy.Controllers

open System
open System.Net

open Microsoft.AspNetCore.Mvc

open SatRadioProxy

type HomeController () =
    inherit Controller()

    member this.Index () =
        this.View()

    member this.SetBookmarks (id) =
        BookmarkManager.setBookmarks id
        this.RedirectToAction(nameof Index)

    member this.PlayChannel (num) =
        let id =
            SiriusXMChannelProvider.channels
            |> Seq.where (fun c -> c.number = num)
            |> Seq.map (fun c -> c.id)
            |> Seq.tryHead
            |> Option.defaultWith (fun () -> raise (StatusCodeException HttpStatusCode.NotFound))

        this.Redirect($"http://{NetworkInterfaceProvider.address}:5000/Proxy/{id}.m3u8")

    member this.PlayBookmark (num) =
        let bookmarks = BookmarkManager.getBookmarks ()

        let bookmark =
            bookmarks
            |> List.tryItem num
            |> Option.defaultWith (fun () -> raise (StatusCodeException HttpStatusCode.NotFound))

        this.Redirect(bookmark)

    [<HttpPost>]
    member this.RefreshChannels () = task {
        do! SiriusXMChannelProvider.refreshChannelsAsync ()
        return this.RedirectToAction(nameof Index)
    }

    [<HttpPost>]
    member this.UpdateIPAddress () = task {
        do! NetworkInterfaceProvider.updateAddressAsync ()
        return this.RedirectToAction(nameof Index)
    }

    [<HttpPost>]
    member this.SetCredentials (username, password) =
        if [username; password] |> List.exists String.IsNullOrEmpty then
            raise (StatusCodeException HttpStatusCode.BadRequest)

        SiriusXMPythonScriptManager.setCredentials (username, password)
        this.RedirectToAction(nameof Index)
