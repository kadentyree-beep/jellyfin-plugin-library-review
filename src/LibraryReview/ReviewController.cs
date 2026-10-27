using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryReview;

[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("LibraryReview")]
public sealed class ReviewController(ReviewService service) : ControllerBase
{
    private JsonResult Json(object value) => new(value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Json(await service.Read(ct));
    [HttpPost("Scan")]
    public async Task<IActionResult> Scan(CancellationToken ct) => await Safe(async () => { await service.Scan(ct); return "Scan completed."; });
    [HttpPost("{id:guid}/Decision")]
    public async Task<IActionResult> Decide(Guid id, ReviewDecision decision, CancellationToken ct) => await Safe(async () => { await service.Decide(id, decision.Status, ct); return "Review saved."; });
    [HttpPost("{id:guid}/Preview")]
    public async Task<IActionResult> Preview(Guid id, CancellationToken ct) => await Safe(async () => await service.Prepare(id, ct));
    [HttpPost("{id:guid}/Replace")]
    public async Task<IActionResult> Replace(Guid id, ConfirmRequest request, CancellationToken ct) => await Safe(async () => await service.Replace(id, request, ct));
    private async Task<IActionResult> Safe(Func<Task<object>> work)
    {
        // Require explicit token authentication for writes, preventing cookie-only CSRF.
        if (!Request.Headers.ContainsKey("X-Emby-Authorization") && !Request.Headers.ContainsKey("Authorization") && !Request.Headers.ContainsKey("X-Emby-Token"))
            return BadRequest(new { message = "Explicit Jellyfin authentication header required." });
        try { return Json(await work()); }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or HttpRequestException or System.Text.Json.JsonException)
        {
            // No upstream URLs, keys, stack traces, or response bodies in client errors.
            return StatusCode(409, new { message = e is InvalidOperationException ? e.Message : "Operation stopped. Check the action log and filesystem before retrying." });
        }
    }
}
