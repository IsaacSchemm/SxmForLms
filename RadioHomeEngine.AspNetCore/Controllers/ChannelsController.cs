using Microsoft.AspNetCore.Mvc;
using Microsoft.FSharp.Core;
using RadioHomeEngine.AspNetCore.Models;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class ChannelsController : Controller
    {
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var allChannels = await ChannelListing.ListChannelsAsync(cancellationToken);

            var knownPlayers = await LyrionKnownPlayers.Names.getPlayersWithNamesAsync();

            return View(new ChannelsModel
            {
                Channels = [
                    .. allChannels.Select(c => new ChannelsModel.Channel
                    {
                        Category = c.category,
                        Name = c.text,
                        ImageSrc = c.icon,
                        Url = c.url,
                        SiriusXMChannelNumber = c.num
                    })
                ],
                Players = [
                    .. knownPlayers.Select(k => new ChannelsModel.Player {
                        MacAddress = k.player.Item,
                        Name = k.name
                    }),
                    .. Roku.GetDevices().Select(device => new ChannelsModel.Player {
                        MacAddress = device.MacAddress,
                        Name = device.Name
                    })
                ]
            });
        }

        [HttpPost]
        public async Task<IActionResult> Play(string mac, string url, string name, CancellationToken cancellationToken)
        {
            foreach (var player in LyrionKnownPlayers.known)
            {
                if (player.Item == mac)
                {
                    await LyrionCLI.Playlist.playItemAsync(player, url, name);
                    return Accepted();
                }
            }

            foreach (var device in Roku.GetDevices())
            {
                if (device.MacAddress == mac)
                {
                    await Roku.PlayAsync(device, url, name, cancellationToken);
                    return Accepted();
                }
            }

            return NoContent();
        }
    }
}
