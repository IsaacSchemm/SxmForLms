using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SatRadioProxy.Streaming;
using System.Text;

namespace SatRadioProxy.AspNetCore.Controllers
{
    public class ProxyController : Controller
    {
        [Route("Proxy/playlist-{id}.m3u8")]
        public async Task<IActionResult> Playlist(string id, string path, CancellationToken cancellationToken)
        {
            string contents = await MediaProxy.getPlaylistAsync(
                id,
                cancellationToken);

            return Content(
                contents,
                "application/x-mpegURL",
                Encoding.UTF8);
        }

        [Route("Proxy/chunklist-{guid}.m3u8")]
        public async Task<IActionResult> Chunklist(Guid guid, CancellationToken cancellationToken)
        {
            string contents = await MediaProxy.getChunklistAsync(
                guid,
                cancellationToken);

            return Content(
                contents,
                "application/x-mpegURL",
                Encoding.UTF8);
        }

        [Route("Proxy/chunk-{guid}-{sequenceNumber}.ts")]
        public async Task<IActionResult> Chunk(Guid guid, UInt128 sequenceNumber, CancellationToken cancellationToken)
        {
            var data = await MediaProxy.getChunkAsync(
                guid,
                sequenceNumber,
                cancellationToken);

            return File(
                data,
                "video/mp2t");
        }
    }
}
