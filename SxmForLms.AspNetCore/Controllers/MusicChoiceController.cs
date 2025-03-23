using Microsoft.AspNetCore.Mvc;

namespace SxmForLms.AspNetCore.Controllers
{
    public class MusicChoiceController : Controller
    {
        public IActionResult ChannelImage()
        {
            return Redirect("https://webplayer.musicchoice.com/Img/App/MC-logo-sml.png");
        }

        public async Task<IActionResult> PlayChannel(int channelId)
        {
            var channels = await MusicChoiceClient.getChannelsAsync();
            var channel = channels
                .Where(c => c.ChannelID == channelId)
                .First();
            var content = await MusicChoiceClient.getContentAsync(channel.ContentId);
            Console.WriteLine($"[{content}]");
            return Redirect(content);
        }
    }
}
