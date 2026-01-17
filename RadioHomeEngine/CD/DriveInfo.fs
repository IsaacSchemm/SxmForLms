namespace RadioHomeEngine

type TrackInfo = {
    title: string
    position: int
}

type DiscInfo = {
    title: string
    artists: string list
    tracks: TrackInfo list
}

type DriveInfo = {
    driveNumber: int
    discid: string option
    disc: DiscInfo
}
