using Microsoft.FSharp.Collections;

namespace RadioHomeEngine.AspNetCore.Models
{
    public record ChannelsModel
    {
        public required FSharpList<Channel> Channels { get; init; }
        public required FSharpList<Player> Players { get; init; }

        public record Channel
        {
            public required string Category { get; init; }
            public required string Name { get; init; }
            public required string ImageSrc { get; init; }
            public required string? Url { get; init; }
        }
        public record Player
        {
            public required string MacAddress { get; init; }
            public required string Name { get; init; }
        }
    }
}
