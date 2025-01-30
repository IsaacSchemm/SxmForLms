# SxmForLms

An ASP.NET Core application that makes SiriusXM channels available to web browsers and Squeezebox devices on the local network.

SxmForLms is designed to run on a Linux server that is also running [Lyrion Music Server](https://lyrion.org/). It manages the "SiriusXM" favorites folder on connected Squeezebox devices, provides decrypted HLS proxy playlists for each numbered SiriusXM station, and provides a web application (on HTTP port 5000) that allows any browser with HLS support (or any browser that can run [hls.js](https://github.com/video-dev/hls.js)) to play these stations.

*Do not expose this application to the internet* - doing so would present both copyright and security issues.

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
* If Lyrion Media Server is installed to `/var/lib/squeezeboxserver`:
    * Periodically updates LMS favorites with the current list of SiriusXM stations (placed in a "SiriusXM" folder)
