namespace SxmForLms

module LyrionIR =
    module Slim =
        let ``0`` = 0x76899867
        let ``1`` = 0x7689f00f
        let ``2`` = 0x768908f7
        let ``3`` = 0x76898877
        let ``4`` = 0x768948b7
        let ``5`` = 0x7689c837
        let ``6`` = 0x768928d7
        let ``7`` = 0x7689a857
        let ``8`` = 0x76896897
        let ``9`` = 0x7689e817

        let arrow_down = 0x7689b04f
        let arrow_left = 0x7689906f
        let arrow_right = 0x7689d02f
        let arrow_up = 0x7689e01f
        let voldown = 0x768900ff
        let volup = 0x7689807f
        let power = 0x768940bf
        let rew = 0x7689c03f
        let pause = 0x768920df
        let fwd = 0x7689a05f
        let add = 0x7689609f
        let play = 0x768910ef
        let search = 0x768958a7
        let shuffle = 0x7689d827
        let repeat = 0x768938c7
        let sleep = 0x7689b847
        let now_playing = 0x76897887
        let size = 0x7689f807
        let brightness = 0x768904fb
        let favorites = 0x768918e7
        let browse = 0x7689708f
        let power_on = 0x76898f70
        let power_off = 0x76898778
        let home = 0x768922dd

        let now_playing_2 = 0x7689a25d
        let search_2 = 0x7689629d
        let favorites_2 = 0x7689e21d

        let menu_browse_album = 0x76897c83
        let menu_browse_artist = 0x7689748b
        let menu_browse_playlists = 0x76897a85
        let menu_browse_music = 0x7689728d

        let menu_search_artist = 0x768954ab
        let menu_search_album = 0x76895ca3
        let menu_search_song = 0x768952ad

        let ``digital_input_aes-ebu`` = 0x768906f9
        let ``digital_input_bnc-spdif`` = 0x76898679
        let ``digital_input_rca-spdif`` = 0x768946b9
        let ``digital_input_toslink`` = 0x7689c639

        let analog_input_line_in = 0x76890ef1

        let muting = 0x7689c43b

        let preset_1 = 0x76898a75
        let preset_2 = 0x76894ab5
        let preset_3 = 0x7689ca35
        let preset_4 = 0x76892ad5
        let preset_5 = 0x7689aa55
        let preset_6 = 0x76896a95

    type Action = Simulate of int | Button of string | Debug of string

    let CustomMappings = Map.ofList [
        0x61a0f00f, Debug "Power"
        0x61a0b847, Simulate Slim.analog_input_line_in

        0x61a000ff, Simulate Slim.``1``
        0x61a0807f, Simulate Slim.``2``
        0x61a040bf, Simulate Slim.``3``
        0x61a0c03f, Simulate Slim.``4``
        0x61a020df, Simulate Slim.``5``
        0x61a0a05f, Simulate Slim.``6``
        0x61a0609f, Simulate Slim.``7``
        0x61a0e01f, Simulate Slim.``8``
        0x61a010ef, Simulate Slim.``9``
        0x61a0827d, Debug "."
        0x61a0906f, Simulate Slim.``0``
        0x61a008f7, Simulate Slim.repeat

        0x61a028d7, Simulate Slim.home // Menu
        0x61a09d62, Simulate Slim.home
        0x61a0d827, Simulate Slim.arrow_left // Exit
        0x61a0e817, Simulate Slim.now_playing // Info*

        0x61a042bd, Simulate Slim.arrow_up
        0x61a0c23d, Simulate Slim.arrow_down
        0x61a06897, Simulate Slim.arrow_left
        0x61a0a857, Simulate Slim.arrow_right
        0x61a018e7, Button "knob_push"

        0x61a022dd, Debug "Aspect"
        0x61a038c7, Debug "CCD"

        0x61a030cf, Simulate Slim.volup
        0x61a0b04f, Simulate Slim.voldown
        0x61a0708f, Simulate Slim.muting

        0x61a050af, Debug "Channel up"
        0x61a0d02f, Debug "Channel down"

        0x61a0c837, Simulate Slim.sleep
        0x61a0d22d, Simulate Slim.favorites
        0x61a08877, Debug "MTS/SAP"
        0x61a0926d, Debug "Picture"

        0x61a00ef1, Simulate Slim.play
        0x61a0817e, Simulate Slim.pause
        0x61a08e71, Button "stop"
        0x61a012ed, Debug "Audio"

        0x61a07e81, Simulate Slim.rew
        0x61a0be41, Simulate Slim.fwd
        0x61a001fe, Button "jump_rew"
        0x61a0fe01, Button "jump_fwd"
    ]
