namespace SxmForLms

open System
open System.Runtime.Caching
open System.Threading

open LyrionCLI

module Reader =
    type Readable = {
        screen: string
        speech: Guid
    }

    let storeSpeech (text: string) =
        let guid = Guid.NewGuid()
        MemoryCache.Default.Set($"{guid}", text, DateTime.UtcNow.AddDays(1))
        guid

    let retrieveSpeech (guid: Guid) =
        match MemoryCache.Default[$"{guid}"] with
        | :? string as str -> str
        | _ -> ""

    let readAsync (player: Player) (readables: Readable seq) = task {
        let! path = Playlist.getPathAsync player
        let! title = Playlist.getTitleAsync player

        let! address = Network.getAddressAsync CancellationToken.None

        do! Playlist.insertItemAsync player path title

        for r in Seq.rev readables do
            do! Playlist.insertItemAsync player $"http://{address}:{Config.port}/Reader/Speech/{r.speech}" r.screen

        do! Players.simulateButtonAsync player "jump_fwd"
    }
