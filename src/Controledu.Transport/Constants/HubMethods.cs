namespace Controledu.Transport.Constants;

/// <summary>
/// Shared SignalR method names.
/// </summary>
public static class HubMethods
{
    /// <summary>
    /// Server to student command: start or resume file transfer.
    /// </summary>
    public const string FileTransferAssigned = "FileTransferAssigned";

    /// <summary>
    /// Server to student command: force local unpair.
    /// </summary>
    public const string ForceUnpair = "ForceUnpair";

    /// <summary>
    /// Server to teacher event: student registered or updated.
    /// </summary>
    public const string StudentUpserted = "StudentUpserted";

    /// <summary>
    /// Server to teacher event: student disconnected.
    /// </summary>
    public const string StudentDisconnected = "StudentDisconnected";

    /// <summary>
    /// Server to teacher event: frame received.
    /// </summary>
    public const string FrameReceived = "FrameReceived";

    /// <summary>
    /// Server to teacher event: detector alert.
    /// </summary>
    public const string AlertReceived = "AlertReceived";

    /// <summary>
    /// Server to teacher event: student signal (for example hand raise).
    /// </summary>
    public const string StudentSignalReceived = "StudentSignalReceived";

    /// <summary>
    /// Server to student event: detection policy changed.
    /// </summary>
    public const string DetectionPolicyUpdated = "DetectionPolicyUpdated";

    /// <summary>
    /// Server to student command: request immediate diagnostics/dataset export upload.
    /// </summary>
    public const string DetectionExportRequested = "DetectionExportRequested";

    /// <summary>
    /// Server to teacher event: diagnostics export is available for download.
    /// </summary>
    public const string DetectionExportReady = "DetectionExportReady";

    /// <summary>
    /// Server to student command: apply teacher-assigned accessibility profile on local endpoint UI.
    /// </summary>
    public const string AccessibilityProfileAssigned = "AccessibilityProfileAssigned";

    /// <summary>
    /// Server to student command: play teacher text announcement using endpoint TTS.
    /// </summary>
    public const string TeacherTtsRequested = "TeacherTtsRequested";

    /// <summary>
    /// Server to student command: teacher chat message for endpoint overlay.
    /// </summary>
    public const string TeacherChatMessageRequested = "TeacherChatMessageRequested";

    /// <summary>
    /// Server to student command: teacher live caption update for endpoint overlay subtitles.
    /// </summary>
    public const string TeacherLiveCaptionRequested = "TeacherLiveCaptionRequested";

    /// <summary>
    /// Server to teacher event: transfer progress update.
    /// </summary>
    public const string FileProgressUpdated = "FileProgressUpdated";

    /// <summary>
    /// Server to teacher event: full student list update.
    /// </summary>
    public const string StudentListChanged = "StudentListChanged";

    /// <summary>
    /// Server to student command: request/stop remote control session.
    /// </summary>
    public const string RemoteControlSessionCommand = "RemoteControlSessionCommand";

    /// <summary>
    /// Server to student command: remote input action for active session.
    /// </summary>
    public const string RemoteControlInputCommand = "RemoteControlInputCommand";

    /// <summary>
    /// Server to teacher event: remote control session status update from student.
    /// </summary>
    public const string RemoteControlStatusUpdated = "RemoteControlStatusUpdated";

    /// <summary>
    /// Server to teacher event: chat message in teacher-student conversation.
    /// </summary>
    public const string ChatMessageReceived = "ChatMessageReceived";
}

