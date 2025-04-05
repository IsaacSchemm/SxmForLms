﻿using Microsoft.AspNetCore.Mvc;
using System;
using System.Text.RegularExpressions;

namespace SxmForLms.AspNetCore.Controllers
{
    public partial class CDController() : Controller
    {
        public async Task<IActionResult> PlayTrack(int track)
        {
            int offset = 0;

            if (Request.Headers.Range.SingleOrDefault() is string range
                && GetRangePattern().Match(range) is Match match
                && match.Success)
            {
                offset = int.Parse(match.Groups[1].Value);
            }

            var obj = await Icedax.extractWaveAsync(track, offset);

            Response.StatusCode = 200;
            Response.ContentType = "audio/wav";
            Response.ContentLength = obj.length;

            return File(
                obj.stream,
                "audio/wav",
                enableRangeProcessing: false);
        }

        [GeneratedRegex("^bytes=([0-9]+)-([0-9]+)$")]
        private static partial Regex GetRangePattern();
    }
}
