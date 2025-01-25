using Microsoft.AspNetCore.Mvc;
using SatRadioProxy.SiriusXM;
using System.Text;
using System.Text.Json;

namespace SatRadioProxy.Web.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Channels()
        {
            return View();
        }

        public async Task<IActionResult> ChannelInfo(CancellationToken cancellationToken)
        {
            var channels = await SiriusXMChannelCache.getChannelsAsync(cancellationToken);

            var channelInfo = channels.Select(c => new
            {
                c.channelNumber,
                c.name,
                c.mediumDescription,
                image = c.images.images
                        .Where(i => i.name == "color channel logo (on dark)")
                        .Where(i => i.width * 1.0 / i.height == 1.25)
                        .Select(i => i.url)
                        .FirstOrDefault()
            });

            string json = JsonSerializer.Serialize(channelInfo);

            return Content(
                $"var channelInfo = {json};",
                "text/javascript",
                Encoding.UTF8);
        }

        public async Task<IActionResult> PlayChannel(int num, CancellationToken cancellationToken)
        {
            var channels = await SiriusXMChannelCache.getChannelsAsync(cancellationToken);

            var channel = channels
                .Where(c => c.channelNumber == $"{num}")
                .FirstOrDefault();

            return channel != null
                ? Redirect($"/Proxy/playlist-{channel.channelId}.m3u8")
                : NotFound();
        }
    }
}
