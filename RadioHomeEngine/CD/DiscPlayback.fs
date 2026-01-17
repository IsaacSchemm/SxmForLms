namespace RadioHomeEngine

open System
open FSharp.Control

open LyrionCLI

module DiscPlayback =
    let playAsync (scope: DiscDriveScope) player = task {
        do! Players.simulateButtonAsync player "stop"

        do! Players.setDisplayAsync player "Please wait" "Searching for tracks..." (TimeSpan.FromSeconds(999))

        let! drives = Discovery.getAllDiscInfoAsync scope

        do! Players.setDisplayAsync player "Please wait" "Finishing up..." (TimeSpan.FromMilliseconds(1))

        do! Playlist.clearAsync player

        let! address = Network.getAddressAsync ()
        for driveInfo in drives do
            for track in driveInfo.disc.tracks do
                let title =
                    match track.title with
                    | "" -> $"Track {track.position}"
                    | x -> x
                do! Playlist.addItemAsync player $"http://{address}:{Config.port}/CD/PlayTrack?driveNumber={driveInfo.driveNumber}&track={track.position}" title

        do! Playlist.playAsync player
    }
