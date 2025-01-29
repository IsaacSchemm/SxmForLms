using Microsoft.AspNetCore.Mvc;
using SxmForLms.Streaming;
using System.Text;

namespace SxmForLms.AspNetCore.Controllers
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

        [Route("Proxy/chunklist-{id}-{index}.m3u8")]
        public async Task<IActionResult> Chunklist(string id, int index, CancellationToken cancellationToken)
        {
            string contents = await MediaProxy.getChunklistAsync(id, index, cancellationToken);

            return Content(
                contents,
                "application/x-mpegURL",
                Encoding.UTF8);
        }

        [Route("Proxy/chunk-{id}-{index}-{sequenceNumber}.ts")]
        public async Task<IActionResult> Chunk(string id, int index, UInt128 sequenceNumber, CancellationToken cancellationToken)
        {
            var data = await MediaProxy.getChunkAsync(id, index, sequenceNumber, cancellationToken);

            return File(
                data,
                "video/mp2t");
        }
    }
}
