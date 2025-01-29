using Microsoft.FSharp.Collections;

namespace SatRadioProxy.AspNetCore.Models
{
    public record RecentlyPlayingModel
    {
        public required ChannelModel Channel { get; init; }
        public required FSharpList<SongModel> Songs { get; init; }
    }
}
