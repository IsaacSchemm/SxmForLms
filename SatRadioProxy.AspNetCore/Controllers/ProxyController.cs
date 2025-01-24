using Microsoft.AspNetCore.Mvc;
using SatRadioProxy.Streaming;
using System.IO;
using System.Text;

namespace SatRadioProxy.AspNetCore.Controllers
{
    public class ProxyController(Proxy proxy) : Controller
    {
        [Route("Proxy/{id}/{**path}")]
        public async Task<IActionResult> Proxy(string id, string path, CancellationToken cancellationToken)
        {
            if (path == "playlist.m3u8")
            {
                var file = await proxy.GetPlaylistAsync(id, cancellationToken);
                return File(file.content, file.content_type);
            }
            else if (path.EndsWith(".m3u8"))
            {
                var file = await proxy.GetChunklistAsync(id, path, cancellationToken);
                return Content(file.content, file.content_type, Encoding.UTF8);
            }
            else
            {
                var file = await proxy.GetChunkAsync(id, path, cancellationToken);
                return File(file.data, file.content_type);
            }
        }
    }
}
