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

        [HttpPost]
        public IActionResult SetBookmarks(string[] id)
        {
            BookmarkManager.setBookmarks(id);
            return RedirectToAction(nameof(Index));
        }

        public IActionResult GetChannel(int num)
        {
            string ipAddress = NetworkInterfaceProvider.address;
            var channel = SiriusXMChannelCache.channels
                .Where(c => c.channelNumber == $"{num}")
                .FirstOrDefault();
            return channel != null
                ? Ok(new
                {
                    channel.name,
                    channel.mediumDescription
                })
                : NotFound();
        }

        public IActionResult PlayChannel(int num)
        {
            string ipAddress = NetworkInterfaceProvider.address;
            var channel = SiriusXMChannelCache.channels
                .Where(c => c.channelNumber == $"{num}")
                .FirstOrDefault();
            return channel != null
                ? Redirect($"http://{ipAddress}:5000/Proxy/playlist-{channel.channelId}.m3u8")
                : NotFound();
        }

        public IActionResult PlayBookmark(int num)
        {
            string ipAddress = NetworkInterfaceProvider.address;
            var bookmarks = BookmarkManager.getBookmarks();
            var channel = SiriusXMChannelCache.channels
                .OrderBy(c => bookmarks.Contains(c.channelId) ? 1 : 2)
                .ThenBy(c => int.TryParse(c.channelNumber, out int n) ? n : 0)
                .Skip(num - 1)
                .FirstOrDefault();
            return channel != null
                ? Redirect($"http://{ipAddress}:5000/Proxy/playlist-{channel.channelId}.m3u8")
                : NotFound();
        }

        [HttpPost]
        public async Task<IActionResult> RefreshChannels(CancellationToken cancellationToken)
        {
            await SiriusXMChannelCache.refresh(cancellationToken);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateIPAddress()
        {
            await NetworkInterfaceProvider.updateAddress();
            return RedirectToAction(nameof(Index));
        }
    }
}
