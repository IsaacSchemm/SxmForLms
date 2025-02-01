using Microsoft.AspNetCore.Mvc;
using Microsoft.FSharp.Core;
using SxmForLms.AspNetCore.Models;
using System.Text;
using System.Text.Json;

namespace SxmForLms.AspNetCore.Controllers
{
    public class RadioController(IHttpClientFactory httpClientFactory) : Controller
    {
        public async IAsyncEnumerable<object> Index()
        {
            int count = await LyrionCLI.Players.countAsync();
            for (int i = 0; i < count; i++)
            {
                string id = await LyrionCLI.Players.getIdAsync(i);
                //await LyrionCLI.Players.showAsync(id, new LyrionCLI.Players.ShowMessage(
                //    FSharpOption<string>.Some(Environment.MachineName),
                //    FSharpOption<string>.Some(Environment.CurrentDirectory),
                //    FSharpOption<TimeSpan>.Some(TimeSpan.FromSeconds(5)),
                //    FSharpOption<LyrionCLI.Players.Brightness>.Some(LyrionCLI.Players.Brightness.PowerOn),
                //    false,
                //    false));
                await LyrionCLI.Players.setDisplayAsync(
                    id,
                    Environment.MachineName,
                    Environment.CurrentDirectory,
                    TimeSpan.FromSeconds(5));
                await Task.Delay(1111);
                yield return new
                {
                    id,
                    display = await LyrionCLI.Players.getDisplayAsync(id),
                    displaynow = await LyrionCLI.Players.getDisplayNowAsync(id)
                };
            }
        }

        public async Task<IActionResult> ChannelInfo(CancellationToken cancellationToken)
        {
            var channels = await SiriusXMClient.getChannelsAsync(cancellationToken);

            var channelInfo = channels.Select(c => new
            {
                c.channelNumber,
                c.name,
                c.mediumDescription
            });

            string json = JsonSerializer.Serialize(channelInfo);

            return Content(
                $"var channelInfo = {json};",
                "text/javascript",
                Encoding.UTF8);
        }

        public async Task<IActionResult> ChannelImage(int num, CancellationToken cancellationToken)
        {
            var channels = await SiriusXMClient.getChannelsAsync(cancellationToken);

            var imageUrl = channels
                .Where(c => c.channelNumber == $"{num}")
                .SelectMany(c => c.images.images)
                .Where(i => i.name == "color channel logo (on dark)")
                .Where(i => i.width * 1.0 / i.height == 1.25)
                .Select(i => i.url)
                .FirstOrDefault();

            if (imageUrl == null)
            {
                return Content(
                    "<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"/>",
                    "image/svg+xml",
                    Encoding.UTF8);
            }

            using var client = httpClientFactory.CreateClient();
            using var resp = await client.GetAsync(imageUrl, cancellationToken);
            var data = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

            return File(
                data,
                resp.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
        }

        public async Task<IActionResult> PlayChannel(int num, CancellationToken cancellationToken)
        {
            var channels = await SiriusXMClient.getChannelsAsync(cancellationToken);

            var channel = channels
                .Where(c => c.channelNumber == $"{num}")
                .FirstOrDefault();

            return channel != null
                ? Redirect($"/Proxy/playlist-{channel.channelId}.m3u8")
                : NotFound();
        }

        public async Task<IActionResult> NowPlaying(int num, CancellationToken cancellationToken)
        {
            var channels = await SiriusXMClient.getChannelsAsync(cancellationToken);
            var channel = channels
                .Where(c => c.channelNumber == $"{num}")
                .First();

            var playlist = await SiriusXMClient.getPlaylistAsync(
                channel.channelGuid,
                channel.channelId,
                cancellationToken);

            return View(new NowPlayingModel
            {
                Channel = new ChannelModel
                {
                    Name = channel.name,
                    Number = num,
                    Description = channel.mediumDescription,
                },
                Song = playlist.cuts
                    .OrderByDescending(c => c.startTime)
                    .Select(c => new SongModel
                    {
                        Title = c.title,
                        Artist = string.Join(" / ", c.artists.Except([c.title])),
                        Album = c.albums.Select(a => a.title).FirstOrDefault()
                    })
                    .FirstOrDefault()
            });
        }

        public async Task<IActionResult> RecentlyPlaying(int num, CancellationToken cancellationToken)
        {
            var channels = await SiriusXMClient.getChannelsAsync(cancellationToken);
            var channel = channels
                .Where(c => c.channelNumber == $"{num}")
                .First();

            var playlist = await SiriusXMClient.getPlaylistAsync(
                channel.channelGuid,
                channel.channelId,
                cancellationToken);

            return View(new RecentlyPlayingModel
            {
                Channel = new ChannelModel
                {
                    Name = channel.name,
                    Number = num,
                    Description = channel.mediumDescription
                },
                Songs = [
                    ..playlist.cuts
                        .OrderByDescending(c => c.startTime)
                        .Take(10)
                        .Select(c => new SongModel
                        {
                            Title = c.title,
                            Artist = string.Join(" / ", c.artists.Except([c.title])),
                            Album = c.albums.Select(a => a.title).FirstOrDefault(),
                            Image = c.albums.SelectMany(a => a.images).FirstOrDefault()
                        })
                ]
            });
        }
    }
}
