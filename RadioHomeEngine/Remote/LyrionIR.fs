namespace RadioHomeEngine

module LyrionIR =
    let Slim = Map.ofList [
        "0", 0x76899867
        "1", 0x7689f00f
        "2", 0x768908f7
        "3", 0x76898877
        "4", 0x768948b7
        "5", 0x7689c837
        "6", 0x768928d7
        "7", 0x7689a857
        "8", 0x76896897
        "9", 0x7689e817

        "arrow_down", 0x7689b04f
        "arrow_left", 0x7689906f
        "arrow_right", 0x7689d02f
        "arrow_up", 0x7689e01f
        "voldown", 0x768900ff
        "volup", 0x7689807f
        "power", 0x768940bf
        "rew", 0x7689c03f
        "pause", 0x768920df
        "fwd", 0x7689a05f
        "add", 0x7689609f
        "play", 0x768910ef
        "search", 0x768958a7
        "shuffle", 0x7689d827
        "repeat", 0x768938c7
        "sleep", 0x7689b847
        "now_playing", 0x76897887
        "size", 0x7689f807
        "brightness", 0x768904fb
        "favorites", 0x768918e7
        "browse", 0x7689708f
        "power_on", 0x76898f70
        "power_off", 0x76898778
        "home", 0x768922dd

        "now_playing_2", 0x7689a25d
        "search_2", 0x7689629d
        "favorites_2", 0x7689e21d

        "menu_browse_album", 0x76897c83
        "menu_browse_artist", 0x7689748b
        "menu_browse_playlists", 0x76897a85
        "menu_browse_music", 0x7689728d

        "menu_search_artist", 0x768954ab
        "menu_search_album", 0x76895ca3
        "menu_search_song", 0x768952ad

        "``digital_input_aes-ebu``", 0x768906f9
        "``digital_input_bnc-spdif``", 0x76898679
        "``digital_input_rca-spdif``", 0x768946b9
        "``digital_input_toslink``", 0x7689c639

        "analog_input_line_in", 0x76890ef1

        "muting", 0x7689c43b

        "preset_1", 0x76898a75
        "preset_2", 0x76894ab5
        "preset_3", 0x7689ca35
        "preset_4", 0x76892ad5
        "preset_5", 0x7689aa55
        "preset_6", 0x76896a95
    ]

    type Press =
    | Button of string
    | StreamInfo
    | PlayAllTracks
    | Eject
    | Forecast
    | ChannelUp
    | ChannelDown
    | Input

    type HoldAction =
    | Message of string
    | OnHold of Press
    | OnRelease of Press

    type Action =
    | Simulate of string
    | Hold of HoldAction list
    | Press of Press
    | Dot
    | Backspace
    | NoAction

    let RCAProjector = [
        0x00ff15ea, Press (Button "power")
        0x00ff11ee, Press (Button "muting")
        0x00ff09f6, Press (Button "jump_rew")
        0x00ffc13e, Press (Button "jump_fwd")
        0x00ff19e6, Simulate "rew"
        0x00ff41be, Simulate "fwd"
        0x00ffc936, Hold [OnHold (Button "pause"); OnRelease (Button "play")]
        0x00ff39c6, Simulate "voldown"
        0x00ff31ce, Simulate "volup"
        0x00ff6b94, Hold [OnRelease PlayAllTracks; OnHold Forecast] // Flip
        0x00ffe916, Press Input // Source
        0x00ff6996, Press StreamInfo // Zoom
        0x00ff8976, Simulate "home" // Menu
        0x00ff25da, Simulate "arrow_left"
        0x00ffa956, Simulate "arrow_up"
        0x00ff59a6, Simulate "arrow_down"
        0x00ffd926, Simulate "arrow_left"
        0x00ff9966, Simulate "arrow_right"
        0x00ff7986, Press (Button "knob_push")
        0x00ffe11e, Simulate "1"
        0x00ff619e, Simulate "2"
        0x00ffa15e, Simulate "3"
        0x00ffd12e, Simulate "4"
        0x00ff51ae, Simulate "5"
        0x00ff916e, Simulate "6"
        0x00fff10e, Simulate "7"
        0x00ff718e, Simulate "8"
        0x00ffb14e, Simulate "9"
        0x00ff21de, Dot // Recall
        0x00ff49b6, Simulate "0"
        0x00ff29d6, Backspace
    ]

    let MappingsOn = Map.ofList [
        yield! RCAProjector
    ]

    let MappingsOff = Map.ofList [
        yield! RCAProjector
    ]
