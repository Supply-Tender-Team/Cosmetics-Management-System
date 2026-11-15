using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Voice;

namespace TenderSystem.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
public class VoiceSearchController : Controller
{
    private const int MaxUploadBytes = 976384;
    private readonly VoiceTranscriptionService _voice;

    public VoiceSearchController(VoiceTranscriptionService voice) => _voice = voice;

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(1_100_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1_100_000)]
    public async Task<IActionResult> Transcribe(IFormFile? audio)
    {
        if (audio is null || audio.Length < 44 || audio.Length > MaxUploadBytes)
            return BadRequest(new { error = "Record up to 30 seconds of audio and try again." });

        try
        {
            await using var stream = audio.OpenReadStream();
            var text = await _voice.TranscribeAsync(stream, HttpContext.RequestAborted);
            return Json(new { text });
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "Audio must be a valid 16 kHz mono PCM WAV recording of up to 30 seconds." });
        }
        catch (FileNotFoundException)
        {
            return StatusCode(503, new { error = "Voice search is unavailable because its local model is missing." });
        }
        catch (InvalidOperationException)
        {
            return StatusCode(503, new { error = "Voice search is not configured." });
        }
    }
}
