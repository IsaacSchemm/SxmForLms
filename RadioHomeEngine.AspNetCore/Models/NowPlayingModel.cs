namespace RadioHomeEngine.AspNetCore.Models
{
    public record NowPlayingModel
    {
        public required PlayingChannelModel Channel { get; init; }
        public required SongModel? Song { get; init; }
    }
}
