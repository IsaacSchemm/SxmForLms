using Microsoft.AspNetCore.Mvc;

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
        public async Task PlaySiriusXMChannel(string id, int number)
        {
            await AtomicActions.performActionAsync(
                Player.NewPlayer(id),
                AtomicAction.NewPlaySiriusXMChannel(number));
        }

        [HttpPost]
        public async Task PlayBrownNoise(string id)
        {
            await AtomicActions.performActionAsync(
                Player.NewPlayer(id),
                AtomicAction.PlayBrownNoise);
        }

        [HttpPost]
        public async Task StreamInfo(string id)
        {
            await AtomicActions.performActionAsync(
                Player.NewPlayer(id),
                AtomicAction.StreamInfo);

            //var p1 = await Players.getDisplayAsync(
            //    Player.NewPlayer(id));

            //var p2 = await Players.getDisplayAsync(
            //    Player.NewPlayer(id));

            //Console.WriteLine(p1);
            //Console.WriteLine(p2);
        }

        [HttpPost]
        public async Task PlayTrack(string id, int number)
        {
            await AtomicActions.performActionAsync(
                Player.NewPlayer(id),
                AtomicAction.NewPlayTrack(number));
        }

        [HttpPost]
        public async Task Eject(string id)
        {
            await AtomicActions.performActionAsync(
                Player.NewPlayer(id),
                AtomicAction.Eject);
        }

        [HttpPost]
        public async Task Forecast(string id)
        {
            await AtomicActions.performActionAsync(
                Player.NewPlayer(id),
                AtomicAction.Forecast);
        }
    }
}
