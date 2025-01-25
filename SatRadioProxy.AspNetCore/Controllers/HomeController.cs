using Microsoft.AspNetCore.Mvc;
using SatRadioProxy.SiriusXM;

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

        public async Task<IActionResult> PlayChannel(int num, CancellationToken cancellationToken)
        {
            var address = NetworkInterfaceProvider.address;
            var port = NetworkInterfaceProvider.port;

            var channels = await SiriusXMChannelCache.getChannelsAsync(cancellationToken);

            var channel = channels
                .Where(c => c.channelNumber == $"{num}")
                .FirstOrDefault();

            return channel != null
                ? Redirect($"http://{address}:{port}/Proxy/playlist-{channel.channelId}.m3u8")
                : NotFound();
        }
    }
}
