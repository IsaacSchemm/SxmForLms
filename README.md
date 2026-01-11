# Radio Home Engine

An ASP.NET Core application that makes SiriusXM channels available to local
network devices and extends Lyrion Music Server with additional functionality
through the use of a third-party IR remote.

RadioHomeEngine is designed to run on a Linux server that is also running [Lyrion Music Server](https://lyrion.org/) (LMS).
Functionality includes:

* Decoding and forwarding SiriusXM audio streams to devices on the local network
* Playing U.S. weather alerts from `weather.gov`, using `espeak` speech synthesis
* Use of a third-party remote (listening via the LMS telnet interface) to:
    * activate built-in functions by simulating real buttons on the remote or device
    * play audio CDs (using the host computer's CD drive)
    * load presets
    * seek to specific timestamps
    * play SiriusXM channels
        * view the title of the current song/program on the most recently entered SiriusXM channel
* Web interface at HTTP port 5000
    * Channel list, with:
        * A web player for each channel, with a list of recently played tracks
        * Ability to play a channel on a Squeezebox device

*This application's port 5000 should not be exposed to the internet* - like LMS, it is designed only for use within a local home network.

## Build

* Install Visual Studio 2022 Community
* Open the `.sln` file
* Right-click on the project `RadioHomeEngine.AspNetCore` and click Publish

If you're running LMS on an ordinary PC, you'll probably want to create a
single-file, self-contained build for Linux-x64. (Radio Home Engine does not
currently support LMS on Windows.) This executable can then be moved to the
Linux computer, marked as executable, and run, perhaps from `/etc/rc.local`
and/or through `screen`.

## Configuration

The SiriusXM username and password are read from `username.txt` and `password.txt`, respectively.

The user's latitude and longitude (used for weather forecasts) are stored in `location.txt`.
