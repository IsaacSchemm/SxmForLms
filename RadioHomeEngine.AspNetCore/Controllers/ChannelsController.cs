using Microsoft.AspNetCore.Mvc;
using RadioHomeEngine.AspNetCore.Models;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class ChannelsController : Controller
    {
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var channels = await SiriusXMClient.getChannelsAsync(cancellationToken);

            var knownPlayers = await LyrionKnownPlayers.Names.getPlayersWithNamesAsync();

            return View(new ChannelsModel
            {
                Channels = [
                    .. channels.Select(c => new ChannelsModel.Channel
                    {
                        ChannelNumber = int.Parse(c.channelNumber),
                        Name = c.name
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
        public async Task<IActionResult> Play(string mac, int num, string name, CancellationToken cancellationToken)
        {
            var address = await Network.getAddressAsync();
            var url = $"http://{address}:{Config.port}/SXM/PlayChannel?num={num}";

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
