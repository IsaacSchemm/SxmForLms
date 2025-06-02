namespace RadioHomeEngine.AspNetCore.Models
{
    public record PlayableChannelModel
    {
        public required string Category { get; init; }
        public required string Name { get; init; }
        public required string? Url { get; init; }
    }
}
