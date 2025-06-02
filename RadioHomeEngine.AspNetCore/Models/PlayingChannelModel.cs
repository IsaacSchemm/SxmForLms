namespace RadioHomeEngine.AspNetCore.Models
{
    public record PlayingChannelModel
    {
        public required string Name { get; init; }
        public required int Number { get; init; }
        public required string Description { get; init; }
    }
}
