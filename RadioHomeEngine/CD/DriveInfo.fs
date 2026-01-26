namespace RadioHomeEngine

type TrackInfo = {
    title: string
    position: int
}

type AudioDiscInfo = {
    discid: string option
    titles: string list
    artists: string list
    tracks: TrackInfo list
}

type DataDiscInfo = {
    files: string list
}

type DiscInfo =
| AudioDisc of AudioDiscInfo
| DataDisc of DataDiscInfo
| NoDisc

type DriveInfo = {
    device: string
    disc: DiscInfo
} with
    member this.AudioDiscs = [match this.disc with AudioDisc x -> x | _ -> ()]
    member this.DataDiscs = [match this.disc with DataDisc x -> x | _ -> ()]
