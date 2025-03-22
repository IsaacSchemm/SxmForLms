# SxmForLms

An ASP.NET Core application that makes SiriusXM channels available to web browsers and Squeezebox devices on the local network.

SxmForLms is designed to run on a Linux server that is also running [Lyrion Music Server](https://lyrion.org/). It manages the "SiriusXM" favorites folder on connected Squeezebox devices, provides decrypted HLS proxy playlists for each numbered SiriusXM station, and provides a web application (on HTTP port 5000) that allows any browser with HLS support (or any browser that can run [hls.js](https://github.com/video-dev/hls.js)) to play these stations.

*This application should not be exposed to the internet!* It is designed for use within a local home network only.

## Build

* Install Visual Studio 2022 Community
* Open the `.sln` file
* Right-click on the project `SxmForLms.AspNetCore` and click Publish

If you're running LMS on an ordinary PC, you'll probably want to create a single-file, self-contained build for Linux-x64. (SxmForLms does not currently support LMS on Windows, due to some hardcoded file paths and commands.) This executable can then be moved to the Linux computer, marked as executable, and run, perhaps from `/etc/rc.local` and/or through `screen`.

## Configuration

The SiriusXM username and password are read from `username.txt` and `password.txt`, respectively.

## Features

* Makes SiriusXM stations available over HLS on HTTP port 5000 at `/Proxy/playlist-{id}.m3u8`
    * A redirect is available that accepts a SiriusXM channel number at `/Radio/PlayChannel?num={number}`
    * Channel logos are available at `/Radio/ChannelImage?num={number}`
    * Segments are decrypted and recontainerized to MPEG-TS (from AAC) to aid compatibility with old devices
* Provides a web interface to play SiriusXM stations, and see what songs a station has recently played, on HTTP port 5000
* Periodically updates LMS favorites with the current list of SiriusXM stations (placed in a "SiriusXM" folder)
* Adds additional custom IR remote handling

## Custom IR

SxmForLms runs a background service that adds custom IR remote functionality. (This functionality is built on top of LMS and its CLI; it doesn't integrate directly with LMS's own IR configuration.)

SxmForLms maintains a TCP connection to LMS to monitor the radio for infrared remote codes it doesn't recognize. Certain codes will trigger behavior in SxmForLms, depending on the configuration in the file `LyrionIR.fs`. This can include:

* Simulating IR codes from the standard remote
* Simulating button presses on the radio
* Retrieving and displaying the current track from SiriusXM
* Changing SiriusXM channels (channel up / channel down)
* Changing the behavior of the 0-9 buttons on the remote, to:
    * Direct digit entry (default behavior)
    * Load single-digit preset
    * Load multi-digit preset (with prompt)
    * Load multi-digit preset (with prompt)
    * Seek (ss, hh:ss, mm:hh:ss) (with prompt)
    * Play SiriusXM channel (with prompt)
    * Calculator (with prompt)
* Performing one action (such as Mute) when a button is pressed and released, but a separate action (such as Power) when held for five seconds

Functions marked "with prompt" will override the radio's display temporarily so the user can enter a number, timestamp, or expression.
