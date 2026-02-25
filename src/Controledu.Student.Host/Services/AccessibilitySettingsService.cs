using System.Globalization;
using System.Text.Json;
using Controledu.Student.Host.Contracts;
using Controledu.Storage.Stores;

namespace Controledu.Student.Host.Services;

/// <summary>
/// Manages local per-device accessibility profile and preset application.
/// </summary>
public interface IAccessibilitySettingsService
{
    /// <summary>
    /// Returns the effective accessibility profile.
    /// </summary>
    Task<AccessibilityProfileResponse> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a local UI update to the accessibility profile.
    /// </summary>
    Task<AccessibilityProfileResponse> UpdateFromLocalAsync(AccessibilityProfileUpdateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a predefined preset and persists the result.
    /// </summary>
    Task<AccessibilityProfileResponse> ApplyPresetAsync(string presetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a teacher-assigned profile if overrides are allowed.
    /// </summary>
    Task<AccessibilityProfileResponse> ApplyTeacherAssignedAsync(TeacherAssignedAccessibilityProfileRequest request, CancellationToken cancellationToken = default);
}

internal sealed class AccessibilitySettingsService(ISettingsStore settingsStore) : IAccessibilitySettingsService
{
    private const string AccessibilityProfileKey = "student.accessibility.profile.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> AllowedPresets = new(StringComparer.OrdinalIgnoreCase)
    {
        "default",
        "vision",
        "hearing",
        "motor",
        "dyslexia",
        "custom",
    };

    private static readonly HashSet<string> AllowedContrastModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "standard",
        "aa",
        "aaa",
    };

    private static readonly HashSet<string> AllowedColorModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "none",
        "protanopia",
        "deuteranopia",
        "tritanopia",
    };

    public async Task<AccessibilityProfileResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var document = await ReadDocumentAsync(cancellationToken) ?? CreateDefaultDocument();
        return ToResponse(document);
    }

    public async Task<AccessibilityProfileResponse> UpdateFromLocalAsync(AccessibilityProfileUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var current = await ReadDocumentAsync(cancellationToken) ?? CreateDefaultDocument();
        var next = BuildUpdatedDocument(
            current,
            request,
            assignmentSource: "local-ui",
            assignedBy: null,
            assignedAtUtc: null,
            preserveTeacherAssignmentInfo: true);

        await SaveDocumentAsync(next, cancellationToken);
        return ToResponse(next);
    }

    public async Task<AccessibilityProfileResponse> ApplyPresetAsync(string presetId, CancellationToken cancellationToken = default)
    {
        var normalizedPreset = NormalizePreset(presetId);
        var current = await ReadDocumentAsync(cancellationToken) ?? CreateDefaultDocument();
        var template = CreatePresetTemplate(normalizedPreset);

        var next = current with
        {
            ActivePreset = normalizedPreset,
            Ui = template.Ui,
            Features = template.Features,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            AssignmentSource = "preset",
        };

        await SaveDocumentAsync(next, cancellationToken);
        return ToResponse(next);
    }

    public async Task<AccessibilityProfileResponse> ApplyTeacherAssignedAsync(TeacherAssignedAccessibilityProfileRequest request, CancellationToken cancellationToken = default)
    {
        var current = await ReadDocumentAsync(cancellationToken) ?? CreateDefaultDocument();
        if (!current.AllowTeacherOverride)
        {
            throw new InvalidOperationException("Teacher override is disabled for this device profile.");
        }

        var next = BuildUpdatedDocument(
            current,
            request.Profile,
            assignmentSource: "teacher",
            assignedBy: string.IsNullOrWhiteSpace(request.TeacherDisplayName) ? "Teacher" : request.TeacherDisplayName.Trim(),
            assignedAtUtc: DateTimeOffset.UtcNow,
            preserveTeacherAssignmentInfo: false);

        await SaveDocumentAsync(next, cancellationToken);
        return ToResponse(next);
    }

    private async Task<AccessibilityProfileDocument?> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        var raw = await settingsStore.GetAsync(AccessibilityProfileKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<AccessibilityProfileDocument>(raw, JsonOptions);
            return parsed is null ? null : NormalizeDocument(parsed);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task SaveDocumentAsync(AccessibilityProfileDocument document, CancellationToken cancellationToken)
    {
        await settingsStore.SetAsync(
            AccessibilityProfileKey,
            JsonSerializer.Serialize(document, JsonOptions),
            cancellationToken);
    }

    private static AccessibilityProfileDocument BuildUpdatedDocument(
        AccessibilityProfileDocument current,
        AccessibilityProfileUpdateRequest request,
        string assignmentSource,
        string? assignedBy,
        DateTimeOffset? assignedAtUtc,
        bool preserveTeacherAssignmentInfo)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedPreset = NormalizePreset(request.ActivePreset);
        var normalizedUi = NormalizeUi(request.Ui);
        var normalizedFeatures = NormalizeFeatures(request.Features);
        var now = DateTimeOffset.UtcNow;

        return current with
        {
            ActivePreset = normalizedPreset,
            AllowTeacherOverride = request.AllowTeacherOverride,
            Ui = normalizedUi,
            Features = normalizedFeatures,
            AssignmentSource = assignmentSource,
            AssignedBy = preserveTeacherAssignmentInfo && string.Equals(current.AssignmentSource, "teacher", StringComparison.OrdinalIgnoreCase)
                ? current.AssignedBy
                : assignedBy,
            AssignedAtUtc = preserveTeacherAssignmentInfo && string.Equals(current.AssignmentSource, "teacher", StringComparison.OrdinalIgnoreCase)
                ? current.AssignedAtUtc
                : assignedAtUtc,
            UpdatedAtUtc = now,
        };
    }

    private static AccessibilityProfileDocument NormalizeDocument(AccessibilityProfileDocument document)
    {
        var normalizedPreset = NormalizePreset(document.ActivePreset);
        var normalized = document with
        {
            ActivePreset = normalizedPreset,
            Ui = NormalizeUi(document.Ui),
            Features = NormalizeFeatures(document.Features),
            AssignmentSource = string.IsNullOrWhiteSpace(document.AssignmentSource) ? "local-ui" : document.AssignmentSource.Trim(),
            AssignedBy = string.IsNullOrWhiteSpace(document.AssignedBy) ? null : document.AssignedBy.Trim(),
        };

        return normalized;
    }

    private static AccessibilityUiSettingsData NormalizeUi(AccessibilityUiSettingsData? ui)
    {
        if (ui is null)
        {
            return CreatePresetTemplate("default").Ui;
        }

        var contrast = string.IsNullOrWhiteSpace(ui.ContrastMode) ? "standard" : ui.ContrastMode.Trim().ToLowerInvariant();
        if (!AllowedContrastModes.Contains(contrast))
        {
            contrast = "standard";
        }

        var colorMode = string.IsNullOrWhiteSpace(ui.ColorBlindMode) ? "none" : ui.ColorBlindMode.Trim().ToLowerInvariant();
        if (!AllowedColorModes.Contains(colorMode))
        {
            colorMode = "none";
        }

        return new AccessibilityUiSettingsData(
            ScalePercent: Math.Clamp(ui.ScalePercent, 100, 300),
            ContrastMode: contrast,
            InvertColors: ui.InvertColors,
            ColorBlindMode: colorMode,
            DyslexiaFontEnabled: ui.DyslexiaFontEnabled,
            LargeCursorEnabled: ui.LargeCursorEnabled,
            HighlightFocusEnabled: ui.HighlightFocusEnabled);
    }

    private static string NormalizePreset(string? preset)
    {
        var normalized = string.IsNullOrWhiteSpace(preset) ? "default" : preset.Trim().ToLowerInvariant();
        if (!AllowedPresets.Contains(normalized))
        {
            throw new InvalidOperationException($"Unsupported accessibility preset: '{preset}'.");
        }

        return normalized;
    }

    private static AccessibilityUiSettingsData NormalizeUi(AccessibilityUiSettingsRequest? ui)
    {
        if (ui is null)
        {
            return CreatePresetTemplate("default").Ui;
        }

        var contrast = string.IsNullOrWhiteSpace(ui.ContrastMode) ? "standard" : ui.ContrastMode.Trim().ToLowerInvariant();
        if (!AllowedContrastModes.Contains(contrast))
        {
            throw new InvalidOperationException($"Unsupported contrast mode: '{ui.ContrastMode}'.");
        }

        var colorMode = string.IsNullOrWhiteSpace(ui.ColorBlindMode) ? "none" : ui.ColorBlindMode.Trim().ToLowerInvariant();
        if (!AllowedColorModes.Contains(colorMode))
        {
            throw new InvalidOperationException($"Unsupported color mode: '{ui.ColorBlindMode}'.");
        }

        return new AccessibilityUiSettingsData(
            ScalePercent: Math.Clamp(ui.ScalePercent, 100, 300),
            ContrastMode: contrast,
            InvertColors: ui.InvertColors,
            ColorBlindMode: colorMode,
            DyslexiaFontEnabled: ui.DyslexiaFontEnabled,
            LargeCursorEnabled: ui.LargeCursorEnabled,
            HighlightFocusEnabled: ui.HighlightFocusEnabled);
    }

    private static AccessibilityFeatureFlagsData NormalizeFeatures(AccessibilityFeatureFlagsRequest? features)
    {
        if (features is null)
        {
            return CreatePresetTemplate("default").Features;
        }

        return new AccessibilityFeatureFlagsData(
            features.VisualAlertsEnabled,
            features.LargeActionButtonsEnabled,
            features.SimplifiedNavigationEnabled,
            features.SingleKeyModeEnabled,
            features.TtsTeacherMessagesEnabled,
            features.AudioLessonModeEnabled,
            features.LiveCaptionsEnabled,
            features.VoiceCommandsEnabled);
    }

    private static AccessibilityFeatureFlagsData NormalizeFeatures(AccessibilityFeatureFlagsData? features)
    {
        if (features is null)
        {
            return CreatePresetTemplate("default").Features;
        }

        return new AccessibilityFeatureFlagsData(
            features.VisualAlertsEnabled,
            features.LargeActionButtonsEnabled,
            features.SimplifiedNavigationEnabled,
            features.SingleKeyModeEnabled,
            features.TtsTeacherMessagesEnabled,
            features.AudioLessonModeEnabled,
            features.LiveCaptionsEnabled,
            features.VoiceCommandsEnabled);
    }

    private static AccessibilityProfileDocument CreateDefaultDocument()
    {
        var template = CreatePresetTemplate("default");
        return new AccessibilityProfileDocument(
            ActivePreset: "default",
            AllowTeacherOverride: true,
            Ui: template.Ui,
            Features: template.Features,
            AssignmentSource: "local-ui",
            AssignedBy: null,
            AssignedAtUtc: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
    }

    private static AccessibilityProfileDocument CreatePresetTemplate(string presetId)
    {
        var preset = NormalizePreset(presetId);

        return preset switch
        {
            "vision" => new AccessibilityProfileDocument(
                "vision",
                true,
                new AccessibilityUiSettingsData(
                    ScalePercent: 150,
                    ContrastMode: "aaa",
                    InvertColors: false,
                    ColorBlindMode: "none",
                    DyslexiaFontEnabled: false,
                    LargeCursorEnabled: true,
                    HighlightFocusEnabled: true),
                new AccessibilityFeatureFlagsData(
                    VisualAlertsEnabled: true,
                    LargeActionButtonsEnabled: true,
                    SimplifiedNavigationEnabled: true,
                    SingleKeyModeEnabled: false,
                    TtsTeacherMessagesEnabled: true,
                    AudioLessonModeEnabled: false,
                    LiveCaptionsEnabled: false,
                    VoiceCommandsEnabled: false),
                "preset",
                null,
                null,
                DateTimeOffset.UtcNow),

            "hearing" => new AccessibilityProfileDocument(
                "hearing",
                true,
                new AccessibilityUiSettingsData(
                    ScalePercent: 115,
                    ContrastMode: "aa",
                    InvertColors: false,
                    ColorBlindMode: "none",
                    DyslexiaFontEnabled: false,
                    LargeCursorEnabled: false,
                    HighlightFocusEnabled: true),
                new AccessibilityFeatureFlagsData(
                    VisualAlertsEnabled: true,
                    LargeActionButtonsEnabled: false,
                    SimplifiedNavigationEnabled: false,
                    SingleKeyModeEnabled: false,
                    TtsTeacherMessagesEnabled: false,
                    AudioLessonModeEnabled: false,
                    LiveCaptionsEnabled: true,
                    VoiceCommandsEnabled: false),
                "preset",
                null,
                null,
                DateTimeOffset.UtcNow),

            "motor" => new AccessibilityProfileDocument(
                "motor",
                true,
                new AccessibilityUiSettingsData(
                    ScalePercent: 130,
                    ContrastMode: "aa",
                    InvertColors: false,
                    ColorBlindMode: "none",
                    DyslexiaFontEnabled: false,
                    LargeCursorEnabled: true,
                    HighlightFocusEnabled: true),
                new AccessibilityFeatureFlagsData(
                    VisualAlertsEnabled: true,
                    LargeActionButtonsEnabled: true,
                    SimplifiedNavigationEnabled: true,
                    SingleKeyModeEnabled: true,
                    TtsTeacherMessagesEnabled: false,
                    AudioLessonModeEnabled: false,
                    LiveCaptionsEnabled: false,
                    VoiceCommandsEnabled: false),
                "preset",
                null,
                null,
                DateTimeOffset.UtcNow),

            "dyslexia" => new AccessibilityProfileDocument(
                "dyslexia",
                true,
                new AccessibilityUiSettingsData(
                    ScalePercent: 115,
                    ContrastMode: "aa",
                    InvertColors: false,
                    ColorBlindMode: "none",
                    DyslexiaFontEnabled: true,
                    LargeCursorEnabled: false,
                    HighlightFocusEnabled: true),
                new AccessibilityFeatureFlagsData(
                    VisualAlertsEnabled: true,
                    LargeActionButtonsEnabled: false,
                    SimplifiedNavigationEnabled: false,
                    SingleKeyModeEnabled: false,
                    TtsTeacherMessagesEnabled: false,
                    AudioLessonModeEnabled: false,
                    LiveCaptionsEnabled: false,
                    VoiceCommandsEnabled: false),
                "preset",
                null,
                null,
                DateTimeOffset.UtcNow),

            "custom" => new AccessibilityProfileDocument(
                "custom",
                true,
                CreatePresetTemplate("default").Ui,
                CreatePresetTemplate("default").Features,
                "preset",
                null,
                null,
                DateTimeOffset.UtcNow),

            _ => new AccessibilityProfileDocument(
                "default",
                true,
                new AccessibilityUiSettingsData(
                    ScalePercent: 100,
                    ContrastMode: "standard",
                    InvertColors: false,
                    ColorBlindMode: "none",
                    DyslexiaFontEnabled: false,
                    LargeCursorEnabled: false,
                    HighlightFocusEnabled: false),
                new AccessibilityFeatureFlagsData(
                    VisualAlertsEnabled: true,
                    LargeActionButtonsEnabled: false,
                    SimplifiedNavigationEnabled: false,
                    SingleKeyModeEnabled: false,
                    TtsTeacherMessagesEnabled: false,
                    AudioLessonModeEnabled: false,
                    LiveCaptionsEnabled: false,
                    VoiceCommandsEnabled: false),
                "preset",
                null,
                null,
                DateTimeOffset.UtcNow),
        };
    }

    private static AccessibilityProfileResponse ToResponse(AccessibilityProfileDocument document)
    {
        return new AccessibilityProfileResponse(
            ActivePreset: document.ActivePreset,
            AllowTeacherOverride: document.AllowTeacherOverride,
            Ui: new AccessibilityUiSettingsResponse(
                document.Ui.ScalePercent,
                document.Ui.ContrastMode,
                document.Ui.InvertColors,
                document.Ui.ColorBlindMode,
                document.Ui.DyslexiaFontEnabled,
                document.Ui.LargeCursorEnabled,
                document.Ui.HighlightFocusEnabled),
            Features: new AccessibilityFeatureFlagsResponse(
                document.Features.VisualAlertsEnabled,
                document.Features.LargeActionButtonsEnabled,
                document.Features.SimplifiedNavigationEnabled,
                document.Features.SingleKeyModeEnabled,
                document.Features.TtsTeacherMessagesEnabled,
                document.Features.AudioLessonModeEnabled,
                document.Features.LiveCaptionsEnabled,
                document.Features.VoiceCommandsEnabled),
            Metadata: new AccessibilityProfileMetadataResponse(
                document.AssignmentSource,
                document.AssignedBy,
                document.AssignedAtUtc?.ToString("O", CultureInfo.InvariantCulture),
                document.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture)));
    }

    private sealed record AccessibilityProfileDocument(
        string ActivePreset,
        bool AllowTeacherOverride,
        AccessibilityUiSettingsData Ui,
        AccessibilityFeatureFlagsData Features,
        string AssignmentSource,
        string? AssignedBy,
        DateTimeOffset? AssignedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    private sealed record AccessibilityUiSettingsData(
        int ScalePercent,
        string ContrastMode,
        bool InvertColors,
        string ColorBlindMode,
        bool DyslexiaFontEnabled,
        bool LargeCursorEnabled,
        bool HighlightFocusEnabled);

    private sealed record AccessibilityFeatureFlagsData(
        bool VisualAlertsEnabled,
        bool LargeActionButtonsEnabled,
        bool SimplifiedNavigationEnabled,
        bool SingleKeyModeEnabled,
        bool TtsTeacherMessagesEnabled,
        bool AudioLessonModeEnabled,
        bool LiveCaptionsEnabled,
        bool VoiceCommandsEnabled);
}
