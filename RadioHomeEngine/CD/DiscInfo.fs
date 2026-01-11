namespace RadioHomeEngine

type TrackInfo = {
    title: string
    position: int
}

type DiscInfo = {
    driveNumber: int
    title: string
    artists: string list
    tracks: TrackInfo list
    source: string
}
