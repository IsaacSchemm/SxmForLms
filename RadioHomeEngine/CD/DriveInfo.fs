namespace RadioHomeEngine

type TrackInfo = {
    title: string
    position: int
}

type DiscInfo = {
    title: string option
    artists: string list
    tracks: TrackInfo list
} with
    member this.DisplayTitle =
        this.title |> Option.defaultValue "Unknown Album"
    member this.DisplayArtist =
        match this.artists with
        | [] -> "Unknown Artist"
        | artists -> String.concat ", " artists

type DriveInfo = {
    device: string
    discid: string option
    disc: DiscInfo
}
