namespace RadioHomeEngine

type TrackInfo = {
    title: string
    position: int
}

type AudioDiscInfo = {
    discid: string option
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

type DiscInfo =
| AudioDisc of AudioDiscInfo
| DataDisc of files: string list

type DriveInfo = {
    device: string
    disc: DiscInfo
} with
    member this.AudioDiscs = [match this.disc with AudioDisc x -> x | _ -> ()]
    member this.DataDiscs = [match this.disc with DataDisc x -> x | _ -> ()]
