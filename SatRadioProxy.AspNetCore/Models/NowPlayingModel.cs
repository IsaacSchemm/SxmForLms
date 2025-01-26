namespace SatRadioProxy.AspNetCore.Models
{
    public record NowPlayingModel
    {
        public string Title { get; init; } = "";
        public string Artist { get; init; } = "";
        public string? Album { get; init; }
        public string? Image { get; init; }
    }
}
