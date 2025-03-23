using Microsoft.AspNetCore.Mvc;

namespace SxmForLms.AspNetCore.Controllers
{
    public class MusicChoiceController : Controller
    {
        public async Task<IActionResult> PlayChannel(int channelId)
        {
            var channels = await MusicChoiceClient.getChannelsAsync();
            var channel = channels
                .Where(c => c.ChannelID == channelId)
                .First();
            var content = await MusicChoiceClient.getContentAsync(channel.ContentId);
            LyrionIRHandler.storeMusicChoiceId(channelId, content);
            return Redirect(content);
        }
    }
}
