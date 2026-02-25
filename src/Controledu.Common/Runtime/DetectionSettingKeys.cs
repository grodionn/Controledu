namespace Controledu.Common.Runtime;

/// <summary>
/// Shared setting keys for detection-related runtime data.
/// </summary>
public static class DetectionSettingKeys
{
    public const string LastAlert = "student.last.alert";
    public const string LastCheckUtc = "student.detection.last-check-utc";
    public const string LastResult = "student.detection.last-result";
    public const string EffectivePolicyJson = "student.detection.effective-policy";
    public const string LocalPolicyJson = "student.detection.local-policy";
    public const string SelfTestRequest = "student.detection.self-test.request";
    public const string LastModelVersion = "student.detection.last-model-version";
    public const string DataCollectionEnabled = "student.detection.data-collection-enabled";
    public const string HandRaiseRequestedAtUtc = "student.signal.hand-raise.requested-at-utc";
    public const string HandRaiseLastSentAtUtc = "student.signal.hand-raise.last-sent-at-utc";
    public const string RemoteControlRequestJson = "student.remote-control.request-json";
    public const string RemoteControlDecisionJson = "student.remote-control.decision-json";
    public const string ChatHistoryJson = "student.chat.history-json";
    public const string ChatOutgoingQueueJson = "student.chat.outgoing-queue-json";
    public const string ChatPreferencesJson = "student.chat.preferences-json";
}
