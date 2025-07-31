using Microsoft.AspNetCore.Mvc;
using RokuDotNet.Client.Input;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class RokuController : Controller
    {
        public IActionResult Index(string? macAddress = null)
        {
            var device = macAddress == null
                ? Roku.GetDevices().First()
                : Roku.GetDevices().Single(d => d.MacAddress == macAddress);
            return View(device);
        }

        public async Task<IActionResult> Press(string macAddress, SpecialKeys key)
        {
            var device = Roku.GetDevices().Single(d => d.MacAddress == macAddress);
            await device.Input.KeyPressAsync(new PressedKey(key));
            return NoContent();
        }
    }
}
