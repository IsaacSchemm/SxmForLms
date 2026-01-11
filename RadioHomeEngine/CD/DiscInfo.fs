namespace RadioHomeEngine

type TrackInfo = {
    title: string
    position: int
}

type DiscInfo = {
    title: string
    artists: string list
    tracks: TrackInfo list
    source: string
}
