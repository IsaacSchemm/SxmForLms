using Microsoft.AspNetCore.Mvc;

namespace SxmForLms.AspNetCore.Controllers
{
    public class CDController() : Controller
    {
        public async Task<IActionResult> Play(int track)
        {
            var obj = await Icedax.extractWaveAsync(
                track == 0
                ? Icedax.Span.WholeDisc
                : Icedax.Span.NewTrack(track));

            Response.StatusCode = 200;
            Response.ContentType = "audio/wav";
            Response.ContentLength = obj.length;

            return new FileStreamResult(
                obj.stream,
                "audio/wav");
        }
    }
}
