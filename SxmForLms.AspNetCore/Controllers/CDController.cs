using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace SxmForLms.AspNetCore.Controllers
{
    public partial class CDController() : Controller
    {
        private async Task<IActionResult> PlayFull(Icedax.Span span)
        {
            var obj = await Icedax.extractWaveAsync(span, 0);

            Response.StatusCode = 200;
            Response.ContentType = "audio/wav";

            return File(
                obj.stream,
                "audio/wav",
                enableRangeProcessing: false);
        }

        private async Task<IActionResult> PlayPartial(Icedax.Span span, int offset)
        {
            var obj = await Icedax.extractWaveAsync(span, offset);

            Response.StatusCode = 200;
            Response.ContentType = "audio/wav";
            Response.ContentLength = obj.length;

            return File(
                obj.stream,
                "audio/wav",
                enableRangeProcessing: false);
        }

        private async Task<IActionResult> Play(Icedax.Span span)
        {
            if (Request.Headers.Range.SingleOrDefault() is string range
                && GetRangePattern().Match(range) is Match match
                && match.Success)
            {
                int offset = int.Parse(match.Groups[1].Value);
                return await PlayPartial(span, offset);
            }
            else
            {
                return await PlayFull(span);
            }
        }


        public async Task<IActionResult> PlayTrack(int track)
        {
            return await Play(Icedax.Span.NewTrack(track));
        }

        public async Task<IActionResult> PlayWholeDisc()
        {
            return await Play(Icedax.Span.WholeDisc);
        }

        [GeneratedRegex("^bytes=([0-9]+)-([0-9]+)$")]
        private static partial Regex GetRangePattern();
    }
}
