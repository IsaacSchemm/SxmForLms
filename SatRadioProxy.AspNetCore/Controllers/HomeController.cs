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

        public IActionResult PlayChannel(int num)
        {
            string ipAddress = NetworkInterfaceProvider.address;
            var channel = SiriusXMClientManager.channels
                .Where(c => c.siriusChannelNumber == $"{num}")
                .FirstOrDefault();
            return channel != null
                ? Redirect($"http://{ipAddress}:5000/Proxy/{channel.channelId}/playlist.m3u8")
                : NotFound();
        }

        public IActionResult PlayBookmark(int num)
        {
            string ipAddress = NetworkInterfaceProvider.address;
            var bookmarks = BookmarkManager.getBookmarks();
            var channel = SiriusXMClientManager.channels
                .OrderBy(c => bookmarks.Contains(c.channelId) ? 1 : 2)
                .ThenBy(c => c.siriusChannelNumber)
                .Skip(num - 1)
                .FirstOrDefault();
            return channel != null
                ? Redirect($"http://{ipAddress}:5000/Proxy/{channel.channelId}/playlist.m3u8")
                : NotFound();
        }

        [HttpPost]
        public async Task<IActionResult> RefreshChannels()
        {
            await SiriusXMClientManager.refresh_channels();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateIPAddress()
        {
            await NetworkInterfaceProvider.updateAddress();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public IActionResult SetCredentials(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return BadRequest();

            SiriusXMClientManager.setCredentials(username, password);
            return RedirectToAction(nameof(Index));
        }
    }
}
