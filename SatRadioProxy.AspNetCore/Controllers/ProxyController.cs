using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SatRadioProxy.Streaming;
using System.Text;

namespace SatRadioProxy.AspNetCore.Controllers
{
    public class ProxyController(IMemoryCache memoryCache) : Controller
    {
        [Route("Proxy/playlist-{id}.m3u8")]
        public async Task<IActionResult> Playlist(string id, string path, CancellationToken cancellationToken)
        {
            string contents = await MediaProxy.getPlaylistAsync(
                memoryCache,
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
                memoryCache,
                guid,
                cancellationToken);

            return Content(
                contents,
                "application/x-mpegURL",
                Encoding.UTF8);
        }

        [Route("Proxy/chunk-{guid}.ts")]
        public async Task<IActionResult> Chunk(Guid guid, CancellationToken cancellationToken)
        {
            var data = await MediaProxy.getChunkAsync(
                memoryCache,
                guid,
                cancellationToken);

            return File(
                data,
                "video/mp2t");
        }
    }
}
