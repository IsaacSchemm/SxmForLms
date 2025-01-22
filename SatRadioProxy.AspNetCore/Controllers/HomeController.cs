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
            var channel = SiriusXMChannelProvider.channels
                .Where(c => c.number == num)
                .FirstOrDefault();
            return channel != null
                ? Redirect($"http://{ipAddress}:5000/Proxy/{channel.id}.m3u8")
                : NotFound();
        }

        public IActionResult PlayBookmark(int num)
        {
            string ipAddress = NetworkInterfaceProvider.address;
            var bookmarks = BookmarkManager.getBookmarks();
            var channel = SiriusXMChannelProvider.channels
                .OrderBy(c => bookmarks.Contains(c.id) ? 1 : 2)
                .ThenBy(c => c.number)
                .Skip(num - 1)
                .FirstOrDefault();
            return channel != null
                ? Redirect($"http://{ipAddress}:5000/Proxy/{channel.id}.m3u8")
                : NotFound();
        }

        [HttpPost]
        public async Task<IActionResult> RefreshChannels()
        {
            await SiriusXMChannelProvider.refreshChannelsAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateIPAddress()
        {
            await NetworkInterfaceProvider.updateAddressAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public IActionResult SetCredentials(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return BadRequest();

            SiriusXMPythonScriptManager.setCredentials(username, password);
            return RedirectToAction(nameof(Index));
        }
    }
}
