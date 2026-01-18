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
        match this.title, this.tracks with
        | Some title, _ -> title
        | None, [] -> "No disc"
        | None, _ :: _ -> "Unknown album"
    member this.DisplayArtist =
        match this.artists with
        | [] -> "Unknown artist"
        | _ :: _ -> String.concat ", " this.artists

type DriveInfo = {
    device: string
    discid: string option
    disc: DiscInfo
    hasdata: bool
}

type HybridDiscInfo = {
    device: string
    audio: DiscInfo
    data: string list
}
