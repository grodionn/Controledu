using Controledu.Transport.Dto;

namespace Controledu.Student.Host.Contracts;

/// <summary>
/// Local API response containing short-lived in-memory session token.
/// </summary>
public sealed record SessionTokenResponse(string Token);

/// <summary>
/// Student host runtime status for the UI.
/// </summary>
public sealed record StudentStatusResponse(
    bool HasAdminPassword,
    bool IsPaired,
    string DeviceName,
    string? PairedServerName,
    string? PairedServerBaseUrl,
    bool ServerOnline,
    bool MonitoringActive,
    bool AgentAutoStart,
    bool AgentRunning,
    string? LastAlert,
    bool DetectionEnabled,
    bool DataCollectionModeEnabled,
    string? LastDetectionCheckUtc);

/// <summary>
/// Request payload for admin password setup.
/// </summary>
public sealed record SetupAdminPasswordRequest(string Password, string ConfirmPassword, bool EnableAgentAutoStart);

/// <summary>
/// Request payload for discovery scan.
/// </summary>
public sealed record DiscoveryRequest(int? TimeoutMs);

/// <summary>
/// Request payload for pairing with teacher server.
/// </summary>
public sealed record PairingRequest(string Pin, string ServerAddress);

/// <summary>
/// Request payload for secure unpair operation.
/// </summary>
public sealed record UnpairRequest(string AdminPassword);

/// <summary>
/// Request payload for toggling agent autostart.
/// </summary>
public sealed record AgentAutoStartRequest(bool Enabled, string? AdminPassword = null);

/// <summary>
/// Request payload for sensitive actions requiring admin password.
/// </summary>
public sealed record ProtectedActionRequest(string AdminPassword);

/// <summary>
/// Request payload for changing persisted display name.
/// </summary>
public sealed record DeviceNameUpdateRequest(string DeviceName, string AdminPassword);

/// <summary>
/// Request payload for protected host shutdown.
/// </summary>
public sealed record ShutdownRequest(string AdminPassword, bool StopAgent = true);

/// <summary>
/// Device name payload.
/// </summary>
public sealed record DeviceNameResponse(string DeviceName);

/// <summary>
/// Accessibility profile payload persisted per student device.
/// </summary>
public sealed record AccessibilityProfileResponse(
    string ActivePreset,
    bool AllowTeacherOverride,
    AccessibilityUiSettingsResponse Ui,
    AccessibilityFeatureFlagsResponse Features,
    AccessibilityProfileMetadataResponse Metadata);

/// <summary>
/// Request payload for local accessibility profile updates.
/// </summary>
public sealed record AccessibilityProfileUpdateRequest(
    string ActivePreset,
    bool AllowTeacherOverride,
    AccessibilityUiSettingsRequest Ui,
    AccessibilityFeatureFlagsRequest Features);

/// <summary>
/// Request payload to apply a predefined accessibility preset.
/// </summary>
public sealed record AccessibilityPresetApplyRequest(string PresetId);

/// <summary>
/// Loopback-only request used by local agent bridge to apply teacher-assigned accessibility profile.
/// </summary>
public sealed record TeacherAssignedAccessibilityProfileRequest(
    string TeacherDisplayName,
    AccessibilityProfileUpdateRequest Profile);

/// <summary>
/// UI-specific accessibility presentation options.
/// </summary>
public sealed record AccessibilityUiSettingsResponse(
    int ScalePercent,
    string ContrastMode,
    bool InvertColors,
    string ColorBlindMode,
    bool DyslexiaFontEnabled,
    bool LargeCursorEnabled,
    bool HighlightFocusEnabled);

/// <summary>
/// Request payload for UI-specific accessibility presentation options.
/// </summary>
public sealed record AccessibilityUiSettingsRequest(
    int ScalePercent,
    string ContrastMode,
    bool InvertColors,
    string ColorBlindMode,
    bool DyslexiaFontEnabled,
    bool LargeCursorEnabled,
    bool HighlightFocusEnabled);

/// <summary>
/// Feature toggles for accessibility and assistive modes.
/// </summary>
public sealed record AccessibilityFeatureFlagsResponse(
    bool VisualAlertsEnabled,
    bool LargeActionButtonsEnabled,
    bool SimplifiedNavigationEnabled,
    bool SingleKeyModeEnabled,
    bool TtsTeacherMessagesEnabled,
    bool AudioLessonModeEnabled,
    bool LiveCaptionsEnabled,
    bool VoiceCommandsEnabled);

/// <summary>
/// Request payload for accessibility/assistive feature toggles.
/// </summary>
public sealed record AccessibilityFeatureFlagsRequest(
    bool VisualAlertsEnabled,
    bool LargeActionButtonsEnabled,
    bool SimplifiedNavigationEnabled,
    bool SingleKeyModeEnabled,
    bool TtsTeacherMessagesEnabled,
    bool AudioLessonModeEnabled,
    bool LiveCaptionsEnabled,
    bool VoiceCommandsEnabled);

/// <summary>
/// Metadata about how the current accessibility profile was applied.
/// </summary>
public sealed record AccessibilityProfileMetadataResponse(
    string AssignmentSource,
    string? AssignedBy,
    string? AssignedAtUtc,
    string? UpdatedAtUtc);

/// <summary>
/// Student overlay/endpoint chat message.
/// </summary>
public sealed record StudentChatMessageResponse(
    string MessageId,
    string ClientId,
    string SenderRole,
    string SenderDisplayName,
    string Text,
    string TimestampUtc);

/// <summary>
/// Student chat thread snapshot for overlay UI.
/// </summary>
public sealed record StudentChatThreadResponse(
    string ClientId,
    StudentChatPreferencesResponse Preferences,
    IReadOnlyList<StudentChatMessageResponse> Messages);

/// <summary>
/// Chat UI preferences persisted on student endpoint.
/// </summary>
public sealed record StudentChatPreferencesResponse(int FontScalePercent);

/// <summary>
/// Overlay request to send a student chat message.
/// </summary>
public sealed record StudentChatSendRequest(string Text);

/// <summary>
/// Agent bridge request to store teacher message locally for overlay/UI.
/// </summary>
public sealed record TeacherChatLocalDeliveryRequest(
    string ClientId,
    string MessageId,
    string SenderRole,
    string SenderDisplayName,
    string Text,
    string TimestampUtc);

/// <summary>
/// Chat UI preference update request.
/// </summary>
public sealed record StudentChatPreferencesUpdateRequest(int FontScalePercent);

/// <summary>
/// Agent bridge response containing current (non-destructive) outgoing chat queue snapshot.
/// </summary>
public sealed record StudentChatOutboxPeekResponse(IReadOnlyList<StudentTeacherChatMessageDto> Messages);

/// <summary>
/// Agent bridge request confirming successful delivery of student chat messages.
/// </summary>
public sealed record StudentChatOutboxAckRequest(IReadOnlyList<string> MessageIds);

/// <summary>
/// Generic boolean result payload.
/// </summary>
public sealed record OkResponse(bool Ok, string? Message = null);

/// <summary>
/// Local detection status for student UI.
/// </summary>
public sealed record DetectionStatusResponse(
    bool DetectionEnabled,
    bool DataCollectionModeEnabled,
    string? LastCheckUtc,
    string? LastResult,
    string? LastModelVersion,
    double MetadataThreshold,
    double MlThreshold,
    double SampleRate,
    int LocalRetentionDays);

/// <summary>
/// Admin-protected local detection policy override request.
/// </summary>
public sealed record DetectionConfigUpdateRequest(
    string AdminPassword,
    bool Enabled,
    double MetadataThreshold,
    double MlThreshold,
    bool DataCollectionModeEnabled,
    double SampleRate,
    int LocalRetentionDays);

/// <summary>
/// Admin-protected self-test request payload.
/// </summary>
public sealed record DetectionSelfTestRequest(string AdminPassword);

/// <summary>
/// Response for exported diagnostics archive.
/// </summary>
public sealed record DiagnosticsExportResponse(string ArchivePath);

/// <summary>
/// Local projection of discovery result.
/// </summary>
public sealed record DiscoveredServerResponse(
    string ServerId,
    string ServerName,
    string Host,
    int Port,
    string BaseUrl,
    bool IsRecommended)
{
    /// <summary>
    /// Creates local response from transport DTO.
    /// </summary>
    public static DiscoveredServerResponse FromDto(DiscoveredServerDto dto, bool isRecommended) =>
        new(dto.ServerId, dto.ServerName, dto.Host, dto.Port, dto.BaseUrl, isRecommended);
}
