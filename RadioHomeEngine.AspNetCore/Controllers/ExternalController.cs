using Microsoft.AspNetCore.Mvc;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class ExternalController : Controller
    {
        public async Task<IActionResult> PlayChannel(int id)
        {
            ChannelMemory.LastPlayed = ChannelMemory.Channel.NewExternal(id);

            var hls = await ExternalStreamSource.getHlsAsync(id);
            return Redirect(hls);
        }
    }
}
