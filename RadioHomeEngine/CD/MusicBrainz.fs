namespace RadioHomeEngine

open System
open System.Net
open System.Net.Http
open System.Text

module MusicBrainz =
    let private client =
        let c = new HttpClient()
        c.BaseAddress <- new Uri("https://musicbrainz.org/ws/2/")
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json")
        c.DefaultRequestHeaders.UserAgent.ParseAdd(Config.userAgentString)
        c

    let private parseAs<'T> (_: 'T) (json: string) = Json.JsonSerializer.Deserialize<'T>(json)

    let getInfoAsync (discId: string) = task {
        use! discResponse = client.GetAsync($"discid/{discId}?inc=recordings+artist-credits")
        if discResponse.StatusCode = HttpStatusCode.NotFound then
            return None
        else
            let! discJson = discResponse.EnsureSuccessStatusCode().Content.ReadAsStringAsync()

            let disc = discJson |> parseAs {|
                releases = [{|
                    title = ""
                    media = [{|
                        tracks = [{|
                            title = ""
                            position = 0
                        |}]
                    |}]
                    ``artist-credit`` = [{|
                        name = ""
                    |}]
                    id = Some Guid.Empty
                |}]
            |}

            let release = Seq.head disc.releases

            return Some {
                artists = [
                    for artist in release.``artist-credit`` do
                        artist.name
                ]
                title = release.title
                tracks = [
                    for media in release.media do
                        for track in media.tracks do {
                            title = track.title
                            position = track.position
                        }
                ]
            }
    }
