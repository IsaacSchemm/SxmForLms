using Microsoft.AspNetCore.Mvc;

namespace SxmForLms.AspNetCore.Controllers
{
    public class ReaderController : Controller
    {
        public async Task<IActionResult> Speech(Guid id)
        {
            string speech = SxmForLms.Speech.retrieveSpeech(id);
            byte[] data = await SpeechSynthesis.generateWavAsync(speech);
            return File(data, "audio/wav");
        }
    }
}
