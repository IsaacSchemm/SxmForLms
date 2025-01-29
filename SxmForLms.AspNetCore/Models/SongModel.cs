namespace SxmForLms.AspNetCore.Models
{
    public record SongModel
    {
        public required string Title { get; init; }
        public required string Artist { get; init; }
        public required string? Album { get; init; }
        public string? Image { get; init; }
    }
}
