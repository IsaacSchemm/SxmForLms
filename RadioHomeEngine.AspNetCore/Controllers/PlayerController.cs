using Microsoft.AspNetCore.Mvc;
using System.Net;
using static RadioHomeEngine.LyrionCLI;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class PlayerController : Controller
    {
        public IActionResult Index(string id)
        {
            var player = Player.NewPlayer(id);
            return View(player);
        }

        [HttpPost]
        public async Task<IActionResult> StreamInfo(string id)
        {
            await AtomicActions.performActionAsync(
                Player.NewPlayer(id),
                AtomicAction.StreamInfo);

            return await Display(id);
        }

        [HttpPost]
        public async Task<IActionResult> PlayTrack(string id, int number)
        {
            await AtomicActions.performActionAsync(
                Player.NewPlayer(id),
                AtomicAction.NewPlayTrack(number));

            return await Display(id);
        }

        [HttpPost]
        public async Task<IActionResult> Eject(string id)
        {
            await AtomicActions.performActionAsync(
                Player.NewPlayer(id),
                AtomicAction.Eject);

            return await Display(id);
        }

        [HttpPost]
        public async Task<IActionResult> Forecast(string id)
        {
            await AtomicActions.performActionAsync(
                Player.NewPlayer(id),
                AtomicAction.Forecast);

            return await Display(id);
        }

        [HttpPost]
        public async Task<IActionResult> Button(string id, string button)
        {
            await LyrionIRHandler.ProcessPressAsync(
                Player.NewPlayer(id),
                LyrionIR.Press.NewButton(button));

            return await Display(id);
        }

        [HttpPost]
        public async Task<IActionResult> Number(string id, int num)
        {
            await LyrionIRHandler.ProcessPressAsync(
                Player.NewPlayer(id),
                LyrionIR.Press.NewNumber(num));

            return await Display(id);
        }

        [HttpGet]
        public async Task<IActionResult> Display(string id)
        {
            if (Request.Headers.Accept.All(str => str?.StartsWith("text/html") == false))
                return NoContent();

            await Task.Delay(250);

            for (int i = 0; i < 2; i++)
            {
                try
                {
                    if (!LyrionKnownPlayers.known.IsEmpty)
                    {
                        var display = await Players.getDisplayNowAsync(Player.NewPlayer(id));
                        return View("Display", display);
                    }
                }
                catch (NoMatchingResponseException) { }
            }

            return NoContent();
        }
    }
}
