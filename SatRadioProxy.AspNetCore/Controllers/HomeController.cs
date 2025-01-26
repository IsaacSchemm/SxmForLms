using Microsoft.AspNetCore.Mvc;
using SatRadioProxy.SiriusXM;
using System.Text;
using System.Text.Json;

namespace SatRadioProxy.AspNetCore.Controllers
{
    public class HomeController(IHttpClientFactory httpClientFactory) : Controller
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
            var channels = await SiriusXMClient.getChannelsAsync(cancellationToken);

            var channelInfo = channels.Select(c => new
            {
                c.channelNumber,
                c.name,
                c.mediumDescription
            });

            string json = JsonSerializer.Serialize(channelInfo);

            return Content(
                $"var channelInfo = {json};",
                "text/javascript",
                Encoding.UTF8);
        }

        public async Task<IActionResult> ChannelImage(int num, CancellationToken cancellationToken)
        {
            var channels = await SiriusXMClient.getChannelsAsync(cancellationToken);

            var imageUrl = channels
                .Where(c => c.channelNumber == $"{num}")
                .SelectMany(c => c.images.images)
                .Where(i => i.name == "color channel logo (on dark)")
                .Where(i => i.width * 1.0 / i.height == 1.25)
                .Select(i => i.url)
                .FirstOrDefault();

            if (imageUrl == null)
            {
                return Content(
                    "<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"/>",
                    "image/svg+xml",
                    Encoding.UTF8);
            }

            using var client = httpClientFactory.CreateClient();
            using var resp = await client.GetAsync(imageUrl, cancellationToken);
            var data = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

            return File(
                data,
                resp.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
        }

        public async Task<IActionResult> PlayChannel(int num, CancellationToken cancellationToken)
        {
            var channels = await SiriusXMClient.getChannelsAsync(cancellationToken);

            var channel = channels
                .Where(c => c.channelNumber == $"{num}")
                .FirstOrDefault();

            return channel != null
                ? Redirect($"/Proxy/playlist-{channel.channelId}.m3u8")
                : NotFound();
        }
    }
}
