using Controledu.Teacher.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Controledu.Teacher.Server.Controllers;

/// <summary>
/// Receives UI notification requests and forwards them to desktop host shell.
/// </summary>
[ApiController]
[Route("api/desktop")]
public sealed class DesktopNotificationController(
    IDesktopNotificationService desktopNotificationService,
    IAuditService auditService) : ControllerBase
{
    /// <summary>
    /// Pushes a notification to desktop host shell.
    /// </summary>
    [HttpPost("notify")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Notify([FromBody] DesktopNotificationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest("Message is required.");
        }

        var title = string.IsNullOrWhiteSpace(request.Title) ? "Controledu" : request.Title.Trim();
        var message = request.Message.Trim();
        if (message.Length > 300)
        {
            message = message[..300];
        }

        var kind = string.IsNullOrWhiteSpace(request.Kind) ? "info" : request.Kind.Trim().ToLowerInvariant();
        var notification = new DesktopNotificationMessage(
            Title: title,
            Message: message,
            Kind: kind,
            TimestampUtc: DateTimeOffset.UtcNow);

        desktopNotificationService.Publish(notification);
        await auditService.RecordAsync("desktop_notification", "teacher-ui", $"kind={kind}; title={title}", cancellationToken);
        return Ok(new { ok = true });
    }
}

/// <summary>
/// Desktop notification request payload.
/// </summary>
public sealed record DesktopNotificationRequest(string? Title, string Message, string? Kind = null);
