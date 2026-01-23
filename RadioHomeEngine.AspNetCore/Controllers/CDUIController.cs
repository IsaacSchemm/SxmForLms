using Microsoft.AspNetCore.Mvc;
using RadioHomeEngine.AspNetCore.Models;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class CDUIController : Controller
    {
        public async Task<IActionResult> Index()
        {
            var drives = await Discovery.getAllDiscInfoAsync(DiscDriveScope.AllDrives);

            return View(new CDsModel
            {
                CDs = [.. drives],
                Players = [
                    .. PlayerConnections.GetAll().Select(conn => new CDsModel.Player {
                        MacAddress = conn.MacAddress,
                        Name = conn.Name
                    })
                ]
            });
        }

        [HttpPost]
        public async Task Clear(string mac)
        {
            await LyrionCLI.Playlist.clearAsync(
                LyrionCLI.Player.NewPlayer(mac));
        }

        [HttpPost]
        public async Task MountCD(string device)
        {
            await DataCD.mountDeviceAsync(device);
        }

        [HttpPost]
        public async Task UnmountCD(string device)
        {
            await DataCD.unmountDeviceAsync(device);
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
