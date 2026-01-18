using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using System.Text.RegularExpressions;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public partial class CDController() : Controller
    {
        public async Task<IActionResult> PlayTrack(string device, int track)
        {
            if (!DiscDrives.exists(device))
                return BadRequest();

            int offset = 0;

            if (Request.Headers.Range.SingleOrDefault() is string range
                && GetRangePattern().Match(range) is Match match
                && match.Success)
            {
                offset = int.Parse(match.Groups[1].Value);
            }

            var obj = await Icedax.extractWaveAsync(device, track, offset);

            Response.StatusCode = 200;
            Response.ContentType = "audio/wav";
            Response.ContentLength = obj.length;

            return File(
                obj.stream,
                "audio/wav",
                enableRangeProcessing: false);
        }

        public async Task<IActionResult> GetFile(string device, string path)
        {
            if (!DiscDrives.exists(device))
                return BadRequest();

            var filename = Path.GetFileName(path);

            var contentType =
                new FileExtensionContentTypeProvider().TryGetContentType(
                    filename,
                    out var foundType)
                ? foundType
                : "application/octet-stream";

            var data = await DataCD.readFileAsync(device, path);

            return File(
                data,
                contentType,
                fileDownloadName: filename);
        }

        [GeneratedRegex("^bytes=([0-9]+)-([0-9]+)$")]
        private static partial Regex GetRangePattern();
    }
}
