using Microsoft.AspNetCore.Mvc;
using SatRadioProxy.Streaming;
using System.Text;

namespace SatRadioProxy.AspNetCore.Controllers
{
    public class ProxyController(Proxy proxy) : Controller
    {
        [Route("Proxy/{id}.m3u8")]
        public async Task<IActionResult> Chunklist(string id, CancellationToken cancellationToken)
        {
            var file = await proxy.GetChunklistAsync(id, cancellationToken);
            return Content(file.content, file.contentType, Encoding.UTF8);
        }

        [Route("Proxy/{**path}")]
        public async Task<IActionResult> Chunk(string path, CancellationToken cancellationToken)
        {
            var file = await proxy.GetChunkAsync(path, cancellationToken);
            return File(file.data, file.contentType);
        }
    }
}
