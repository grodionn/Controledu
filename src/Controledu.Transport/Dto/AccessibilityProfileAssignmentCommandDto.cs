namespace Controledu.Transport.Dto;

/// <summary>
/// Teacher-assigned accessibility profile command delivered to student agent via SignalR.
/// </summary>
public sealed record AccessibilityProfileAssignmentCommandDto(
    string ClientId,
    string TeacherDisplayName,
    AccessibilityProfileUpdateDto Profile);

/// <summary>
/// Shared transport payload for accessibility profile update shape.
/// </summary>
public sealed record AccessibilityProfileUpdateDto(
    string ActivePreset,
    bool AllowTeacherOverride,
    AccessibilityUiSettingsDto Ui,
    AccessibilityFeatureFlagsDto Features);

/// <summary>
/// Shared transport DTO for UI accessibility settings.
/// </summary>
public sealed record AccessibilityUiSettingsDto(
    int ScalePercent,
    string ContrastMode,
    bool InvertColors,
    string ColorBlindMode,
    bool DyslexiaFontEnabled,
    bool LargeCursorEnabled,
    bool HighlightFocusEnabled);

/// <summary>
/// Shared transport DTO for assistive feature flags.
/// </summary>
public sealed record AccessibilityFeatureFlagsDto(
    bool VisualAlertsEnabled,
    bool LargeActionButtonsEnabled,
    bool SimplifiedNavigationEnabled,
    bool SingleKeyModeEnabled,
    bool TtsTeacherMessagesEnabled,
    bool AudioLessonModeEnabled,
    bool LiveCaptionsEnabled,
    bool VoiceCommandsEnabled);

