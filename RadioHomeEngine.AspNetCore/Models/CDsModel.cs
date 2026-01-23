using Microsoft.FSharp.Collections;

namespace RadioHomeEngine.AspNetCore.Models
{
    public record CDsModel
    {
        public required FSharpList<DriveInfo> CDs { get; init; }
        public required FSharpList<Player> Players { get; init; }

        public record Player
        {
            public required string MacAddress { get; init; }
            public required string Name { get; init; }
        }
    }
}
