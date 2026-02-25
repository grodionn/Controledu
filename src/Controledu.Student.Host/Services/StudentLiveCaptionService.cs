using System.Globalization;
using Controledu.Student.Host.Contracts;

namespace Controledu.Student.Host.Services;

/// <summary>
/// Holds current teacher live-caption text for the student overlay (ephemeral, in-memory).
/// </summary>
public interface IStudentLiveCaptionService
{
    /// <summary>
    /// Returns current caption payload (or hidden/empty state if expired).
    /// </summary>
    Task<StudentLiveCaptionResponse> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies teacher caption update delivered by Student.Agent bridge.
    /// </summary>
    Task<StudentLiveCaptionResponse> ApplyTeacherCaptionAsync(TeacherLiveCaptionLocalDeliveryRequest request, CancellationToken cancellationToken = default);
}

internal sealed class StudentLiveCaptionService : IStudentLiveCaptionService
{
    private readonly SemaphoreSlim _sync = new(1, 1);
    private LiveCaptionState? _current;

    public async Task<StudentLiveCaptionResponse> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (_current is null)
            {
                return Empty();
            }

            var now = DateTimeOffset.UtcNow;
            if (_current.ExpiresAtUtc <= now)
            {
                _current = null;
                return Empty();
            }

            return ToResponse(_current);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<StudentLiveCaptionResponse> ApplyTeacherCaptionAsync(TeacherLiveCaptionLocalDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (request.Clear || string.IsNullOrWhiteSpace(request.Text))
            {
                _current = null;
                return Empty();
            }

            if (!DateTimeOffset.TryParse(request.TimestampUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestampUtc))
            {
                timestampUtc = DateTimeOffset.UtcNow;
            }

            var ttlMs = Math.Clamp(request.TtlMs, 1000, 15000);
            var teacher = string.IsNullOrWhiteSpace(request.TeacherDisplayName) ? "Teacher" : request.TeacherDisplayName.Trim();
            var caption = request.Text.Trim();
            if (caption.Length > 400)
            {
                caption = caption[..400];
            }

            _current = new LiveCaptionState(
                CaptionId: string.IsNullOrWhiteSpace(request.CaptionId) ? Guid.NewGuid().ToString("N") : request.CaptionId.Trim(),
                TeacherDisplayName: teacher,
                LanguageCode: string.IsNullOrWhiteSpace(request.LanguageCode) ? null : request.LanguageCode.Trim(),
                Text: caption,
                IsFinal: request.IsFinal,
                Sequence: request.Sequence,
                TimestampUtc: timestampUtc,
                ExpiresAtUtc: DateTimeOffset.UtcNow.AddMilliseconds(ttlMs));

            return ToResponse(_current);
        }
        finally
        {
            _sync.Release();
        }
    }

    private static StudentLiveCaptionResponse Empty() =>
        new(
            Visible: false,
            CaptionId: string.Empty,
            TeacherDisplayName: "Teacher",
            LanguageCode: null,
            Text: string.Empty,
            IsFinal: true,
            Sequence: 0,
            TimestampUtc: null,
            ExpiresAtUtc: null);

    private static StudentLiveCaptionResponse ToResponse(LiveCaptionState state) =>
        new(
            Visible: true,
            CaptionId: state.CaptionId,
            TeacherDisplayName: state.TeacherDisplayName,
            LanguageCode: state.LanguageCode,
            Text: state.Text,
            IsFinal: state.IsFinal,
            Sequence: state.Sequence,
            TimestampUtc: state.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
            ExpiresAtUtc: state.ExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture));

    private sealed record LiveCaptionState(
        string CaptionId,
        string TeacherDisplayName,
        string? LanguageCode,
        string Text,
        bool IsFinal,
        long Sequence,
        DateTimeOffset TimestampUtc,
        DateTimeOffset ExpiresAtUtc);
}
