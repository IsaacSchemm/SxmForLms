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

    type CustomAction =
    | ChannelUp
    | ChannelDown
    | Input
    | Backspace

    type Press =
    | Button of string
    | Custom of CustomAction
    | Atomic of AtomicAction
    | IRPress of string
    | Number of int

    type Action =
    | IR of string
    | Series of Press list
    | NoAction

    let RCAProjector = [
        0x00ff15ea, Series [Button "power"]
        0x00ff11ee, IR "muting"
        0x00ff09f6, Series [Button "jump_rew"]
        0x00ffc13e, Series [Button "jump_fwd"]
        0x00ff19e6, IR "rew"
        0x00ff41be, IR "fwd"
        0x00ffc936, Series [IRPress "play"; IRPress "pause"]
        0x00ff39c6, IR "voldown"
        0x00ff31ce, IR "volup"
        0x00ff6b94, Series [Atomic Forecast; Atomic PlayBrownNoise] // Flip
        0x00ffe916, Series [Custom Input; Button "stop"] // Source
        0x00ff6996, Series [Atomic StreamInfo] // Zoom
        0x00ff8976, IR "home" // Menu
        0x00ff25da, Series [Button "exit_left"]
        0x00ffa956, IR "arrow_up"
        0x00ff59a6, IR "arrow_down"
        0x00ffd926, IR "arrow_left"
        0x00ff9966, IR "arrow_right"
        0x00ff7986, Series [Button "knob_push"]
        0x00ffe11e, Series [Number 1]
        0x00ff619e, Series [Number 2]
        0x00ffa15e, Series [Number 3]
        0x00ffd12e, Series [Number 4]
        0x00ff51ae, Series [Number 5]
        0x00ff916e, Series [Number 6]
        0x00fff10e, Series [Number 7]
        0x00ff718e, Series [Number 8]
        0x00ffb14e, Series [Number 9]
        0x00ff21de, NoAction // Recall
        0x00ff49b6, Series [Number 0]
        0x00ff29d6, Series [Custom Backspace; IRPress "favorites"] // Fav/⌫
    ]

    let Mappings = Map.ofList RCAProjector
