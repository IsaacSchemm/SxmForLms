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

            var drives = await Discovery.getAllDiscInfoAsync(DiscDriveScope.AllDrives);

            return View(new ChannelsModel
            {
                CDs = [.. drives],
                Channels = await channelsTask,
                Players = [
                    .. PlayerConnections.GetAll().Select(conn => new ChannelsModel.Player {
                        MacAddress = conn.MacAddress,
                        Name = conn.Name
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
        public async Task PlayCD(string device, string mac)
        {
            await AtomicActions.performActionAsync(
                LyrionCLI.Player.NewPlayer(mac),
                AtomicAction.NewPlayCD(
                    DiscDriveScope.NewSingleDrive(device)));
        }

        [HttpPost]
        public async Task PlayMP3CD(string device, string mac)
        {
            await AtomicActions.performActionAsync(
                LyrionCLI.Player.NewPlayer(mac),
                AtomicAction.NewPlayMP3CD(
                    DiscDriveScope.NewSingleDrive(device)));
        }

        [HttpPost]
        public void RipCD(string device)
        {
            Abcde.beginRipAsync(
                DiscDriveScope.NewSingleDrive(device));
        }

        [HttpPost]
        public async Task EjectCD(string device)
        {
            await DiscDrives.ejectAsync(
                DiscDriveScope.NewSingleDrive(device));
        }
    }
}
