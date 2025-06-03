using Microsoft.AspNetCore.Mvc;
using RadioHomeEngine.AspNetCore.Models;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class ChannelsController : Controller
    {
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var allChannels = await ChannelListing.ListChannelsAsync(cancellationToken);

            IReadOnlyList<PlayableChannelModel> channels = [
                .. allChannels.Select(c => new PlayableChannelModel
                {
                    Category = c.category,
                    Name = c.text,
                    ImageSrc = c.icon,
                    Url = c.url
                })
            ];

            return View(channels);
        }
    }
}
