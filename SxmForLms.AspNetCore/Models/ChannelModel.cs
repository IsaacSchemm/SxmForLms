namespace SxmForLms.AspNetCore.Models
{
    public record ChannelModel
    {
        public required string Name { get; init; }
        public required int Number { get; init; }
        public required string Description { get; init; }
    }
}
