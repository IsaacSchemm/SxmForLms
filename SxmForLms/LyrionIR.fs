namespace SxmForLms

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

    type Action =
    | Power
    | Simulate of string
    | Button of string
    | Info
    | EnterPreset
    | EnterSiriusXMChannel
    | NoAction

    let CustomMappings = Map.ofList [
        0x61a0f00f, Power
        0x61a0b847, NoAction // "Input"

        0x61a000ff, Simulate "1"
        0x61a0807f, Simulate "2"
        0x61a040bf, Simulate "3"
        0x61a0c03f, Simulate "4"
        0x61a020df, Simulate "5"
        0x61a0a05f, Simulate "6"
        0x61a0609f, Simulate "7"
        0x61a0e01f, Simulate "8"
        0x61a010ef, Simulate "9"
        0x61a0827d, NoAction // .
        0x61a0906f, Simulate "0"
        0x61a008f7, Simulate "repeat"

        0x61a028d7, Simulate "home" // Menu
        0x61a09d62, Simulate "home"
        0x61a0d827, Button "exit_left"
        0x61a0e817, Info

        0x61a042bd, Simulate "arrow_up"
        0x61a0c23d, Simulate "arrow_down"
        0x61a06897, Simulate "arrow_left"
        0x61a0a857, Simulate "arrow_right"
        0x61a018e7, Button "knob_push"

        0x61a022dd, EnterPreset // Aspect
        0x61a038c7, EnterSiriusXMChannel // CCD

        0x61a030cf, Simulate "volup"
        0x61a0b04f, Simulate "voldown"
        0x61a0708f, Simulate "muting"

        0x61a050af, NoAction // "Channel up"
        0x61a0d02f, NoAction // "Channel down"

        0x61a0c837, Simulate "sleep"
        0x61a0d22d, Simulate "favorites"
        0x61a08877, NoAction // "MTS/SAP"
        0x61a0926d, NoAction // "Picture"

        0x61a00ef1, Simulate "play"
        0x61a0817e, Simulate "pause"
        0x61a08e71, Button "stop"
        0x61a012ed, NoAction // "Audio"

        0x61a07e81, Simulate "rew"
        0x61a0be41, Simulate "fwd"
        0x61a001fe, Button "jump_rew"
        0x61a0fe01, Button "jump_fwd"
    ]
