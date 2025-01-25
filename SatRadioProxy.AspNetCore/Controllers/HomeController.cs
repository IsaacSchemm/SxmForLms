using Microsoft.AspNetCore.Mvc;
using SatRadioProxy.SiriusXM;
using System.Threading;

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

        [HttpPost]
        public IActionResult SetBookmarks(string[] id)
        {
            BookmarkManager.setBookmarks(id);
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> PlayChannel(int num, CancellationToken cancellationToken)
        {
            string ipAddress = NetworkInterfaceProvider.address;

            var channels = await SiriusXMChannelCache.getChannelsAsync(cancellationToken);

            var channel = channels
                .Where(c => c.channelNumber == $"{num}")
                .FirstOrDefault();
            return channel != null
                ? Redirect($"http://{ipAddress}:5000/Proxy/playlist-{channel.channelId}.m3u8")
                : NotFound();
        }

        public async Task<IActionResult> PlayBookmark(int num, CancellationToken cancellationToken)
        {
            string ipAddress = NetworkInterfaceProvider.address;

            var bookmarks = BookmarkManager.getBookmarks();

            var channels = await SiriusXMChannelCache.getChannelsAsync(cancellationToken);

            var channel = channels
                .OrderBy(c => bookmarks.Contains(c.channelId) ? 1 : 2)
                .ThenBy(c => int.TryParse(c.channelNumber, out int n) ? n : 0)
                .Skip(num - 1)
                .FirstOrDefault();

            return channel != null
                ? Redirect($"http://{ipAddress}:5000/Proxy/playlist-{channel.channelId}.m3u8")
                : NotFound();
        }
    }
}
