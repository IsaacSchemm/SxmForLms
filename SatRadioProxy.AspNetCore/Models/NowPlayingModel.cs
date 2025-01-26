using Microsoft.FSharp.Collections;

namespace SatRadioProxy.AspNetCore.Models
{
    public record NowPlayingModel
    {
        public record Song
        {
            public string Title { get; init; } = "";
            public string Artist { get; init; } = "";
            public string? Album { get; init; }
            public string? Image { get; init; }
        }

        public string Name { get; init; } = "";
        public int Number { get; init; }
        public string Description { get; init; } = "";

        public FSharpList<Song> Songs { get; init; } = [];
    }
}
