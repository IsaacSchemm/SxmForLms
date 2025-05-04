using Microsoft.AspNetCore.Mvc;
using System.Net;
using static RadioHomeEngine.LyrionCLI;

namespace RadioHomeEngine.AspNetCore.Controllers
{
    public class PlayerController : Controller
    {
        private const string AUTOHIDE = "<script type='text/javascript'>setTimeout(function(){location.href='about:blank';},3000)</script>";

        public IActionResult Index(string id)
        {
            var player = Player.NewPlayer(id);
            return View(player);
        }

        [HttpPost]
        public async Task<IActionResult> PlaySiriusXMChannel(string id, int number)
        {
            await AtomicActions.performActionAsync(
                Player.NewPlayer(id),
                AtomicAction.NewPlaySiriusXMChannel(number));

            return await Display(id);
        }

        [HttpPost]
        public async Task<IActionResult> PlayBrownNoise(string id)
        {
            await AtomicActions.performActionAsync(
                Player.NewPlayer(id),
                AtomicAction.PlayBrownNoise);

            return await Display(id);
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

        [HttpGet]
        public async Task<IActionResult> Display(string id)
        {
            await Task.Delay(250);

            var display = LyrionKnownPlayers.known.IsEmpty
                ? new("Radio Home Engine", "No Squeezebox players connected")
                : await Players.getDisplayNowAsync(Player.NewPlayer(id));

            return View(display);
        }
    }
}
