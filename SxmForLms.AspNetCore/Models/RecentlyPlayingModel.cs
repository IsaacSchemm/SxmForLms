using Microsoft.FSharp.Collections;

namespace SxmForLms.AspNetCore.Models
{
    public record RecentlyPlayingModel
    {
        public required ChannelModel Channel { get; init; }
        public required FSharpList<SongModel> Songs { get; init; }
    }
}
