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

## Architecture

* **SxmForLms** (F#)
    * **Config**: defines the HTTP port number (default is 5000) and SiriusXM region (US / CA).
    * **NetworkInterfaceProvider**: lets SxmForLms find its own IP address (the IP address that the radio will see); used when updating LMS favorites
    * **LyrionFavoritesManager**: looks for the "SiriusXM" favorites category on LMS; if it does not exist or does not match the current list of SiriusXM channels, it will be updated and LMS will be restarted
    * **SiriusXMClient**: communicates with SiriusXM APIs by simulating a SiriusXM app, and fetches media using SiriusXM cookies that it obtains; essentially an F# port of https://github.com/PaulWebster/SiriusXM/tree/PaulWebster-cookies
    * **ChunklistParser**: parses an HLS chunklist line-by-line, recording all data necessary for MediaProxy to rebuild it with new file paths
    * **MediaProxy**: fetches HLS playlists (given a channel ID), chunklists (given a channel ID and index), and chunks (given a channel ID, index, and sequence number); decrypts chunks and recontainerizes them to MPEG-TS (using `ffmpeg`)
    * **SiriusXMLyrionFavoritesService**: runs as a `BackgroundService` and runs the update method in `LyrionFavoritesManager` every so often
* **SxmForLms.AspNetCore** (C#)
    * **Controllers**
        * **ProxyController**: exposes the proxied media from `MediaProxy` over HTTP
        * **HomeController**: provides the web browser UI
