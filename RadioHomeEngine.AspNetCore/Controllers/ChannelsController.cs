using Microsoft.AspNetCore.Mvc;
using Microsoft.FSharp.Collections;
using RadioHomeEngine.AspNetCore.Models;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class ChannelsController : Controller
    {
        private static async Task<FSharpList<ChannelsModel.Channel>> GetChannelsAsync(CancellationToken cancellationToken)
        {
            FSharpList<ChannelsModel.Channel> channels = [];

            try
            {
                var list = await SiriusXMClient.getChannelsAsync(cancellationToken);

                return [
                    .. list.Select(c => new ChannelsModel.Channel
                    {
                        ChannelNumber = int.Parse(c.channelNumber),
                        Name = c.name
                    })
                ];
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return [];
            }
        }

        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var channelsTask = GetChannelsAsync(cancellationToken);

            var drives = await Discovery.getAllDiscInfoAsync(DiscDrives.allDriveNumbers);

            var knownPlayers = await LyrionKnownPlayers.Names.getPlayersWithNamesAsync();

            return View(new ChannelsModel
            {
                CDs = [.. drives],
                Channels = await channelsTask,
                Players = [
                    .. knownPlayers.Select(k => new ChannelsModel.Player {
                        MacAddress = k.player.Item,
                        Name = k.name
                    })
                ]
            });
        }

        [HttpPost]
        public async Task Play(string mac, int num)
        {
            await AtomicActions.performActionAsync(
                LyrionCLI.Player.NewPlayer(mac),
                AtomicAction.NewPlaySiriusXMChannel(num));
        }

        [HttpPost]
        public async Task Clear(string mac)
        {
            await LyrionCLI.Playlist.clearAsync(
                LyrionCLI.Player.NewPlayer(mac));
        }

        [HttpPost]
        public async Task PlayCD(int driveNumber, string mac)
        {
            await AtomicActions.playDiscsAsync(
                LyrionCLI.Player.NewPlayer(mac),
                [driveNumber]);
        }

        [HttpPost]
        public void RipCD(int driveNumber)
        {
            Abcde.beginRipAsync([driveNumber]);
        }

        [HttpPost]
        public async Task EjectCD(int driveNumber)
        {
            await DiscDrives.ejectAsync(driveNumber);
        }
    }
}
