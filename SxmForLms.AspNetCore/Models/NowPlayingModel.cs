namespace SxmForLms.AspNetCore.Models
{
    public record NowPlayingModel
    {
        public required ChannelModel Channel { get; init; }
        public required SongModel? Song { get; init; }
    }
}
