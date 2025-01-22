namespace SatRadioProxy.Streaming

open System

type Segment = {
    key: string
    headerTags: string list
    mediaSequence: UInt128
    segmentTags: string list
    path: string
}
