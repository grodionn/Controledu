using Controledu.Student.Agent.Models;
using Controledu.Transport.Dto;

namespace Controledu.Student.Agent.Services;

/// <summary>
/// Handles one capture+send cycle for student frame streaming.
/// </summary>
public interface ICaptureCycleService
{
    /// <summary>
    /// Captures one frame and sends it to teacher hub.
    /// </summary>
    Task<CaptureCycleResult> RunAsync(
        ResolvedStudentBinding binding,
        int sequence,
        int fps,
        int jpegQuality,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of one capture+send iteration.
/// </summary>
public sealed record CaptureCycleResult(ScreenCaptureResult? Capture, TimeSpan Duration);

internal sealed class CaptureCycleService(
    IScreenCaptureService screenCaptureService,
    StudentHubClient hubClient,
    ILogger<CaptureCycleService> logger) : ICaptureCycleService
{
    public async Task<CaptureCycleResult> RunAsync(
        ResolvedStudentBinding binding,
        int sequence,
        int fps,
        int jpegQuality,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;

        try
        {
            var capture = await screenCaptureService.CaptureAsync(jpegQuality, cancellationToken);
            if (capture is null)
            {
                return new CaptureCycleResult(null, TimeSpan.Zero);
            }

            var frame = new ScreenFrameDto(
                binding.ClientId,
                capture.Payload,
                capture.Format,
                capture.Width,
                capture.Height,
                sequence,
                DateTimeOffset.UtcNow);

            await hubClient.SendFrameAsync(frame, cancellationToken);
            return new CaptureCycleResult(capture, DateTimeOffset.UtcNow - started);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Frame send failed at FPS {Fps} quality {Quality}", fps, jpegQuality);
            return new CaptureCycleResult(null, TimeSpan.FromSeconds(2));
        }
    }
}
