using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class NoiseController : Controller
    {
        [Route("Noise/playlist.m3u8")]
        public async Task<IActionResult> Playlist(CancellationToken cancellationToken)
        {
            return Content(
                Noise.getPlaylist(),
                "application/x-mpegURL",
                Encoding.UTF8);
        }

        [Route("Noise/chunklist.m3u8")]
        public async Task<IActionResult> Chunklist(CancellationToken cancellationToken)
        {
            return Content(
                Noise.getChunklist(),
                "application/x-mpegURL",
                Encoding.UTF8);
        }

        [Route("Noise/chunk-{_}.ts")]
        public async Task<IActionResult> Chunk(long _, CancellationToken cancellationToken)
        {
            return File(
                await Noise.getChunkAsync(cancellationToken),
                "video/mp2t");
        }
    }
}
