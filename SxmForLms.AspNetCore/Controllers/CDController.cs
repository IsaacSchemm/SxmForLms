using Microsoft.AspNetCore.Mvc;

namespace SxmForLms.AspNetCore.Controllers
{
    public class CDController() : Controller
    {
        public IActionResult Play(int track)
        {
            Console.WriteLine(track);

            var stream = Icedax.extractWave(
                track == 0
                ? Icedax.Span.WholeDisc
                : Icedax.Span.NewTrack(track));

            return new FileStreamResult(
                stream,
                "audio/wav")
            {
                EnableRangeProcessing = true
            };
        }
    }
}
