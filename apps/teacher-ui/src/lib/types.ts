import type { components } from "@controledu/shared-api-contracts/teacher-server";

type TeacherApiSchemas = components["schemas"];
const DEFAULT_DATE_ISO = "1970-01-01T00:00:00.000Z";

export type StudentInfo = {
  clientId: string;
  hostName: string;
  userName: string;
  localIpAddress?: string | null;
  lastSeenUtc: string;
  isOnline: boolean;
  detectionEnabled?: boolean;
  lastDetectionAtUtc?: string | null;
  lastDetectionClass?: string | null;
  lastDetectionConfidence?: number | null;
  alertCount?: number;
};

export type ScreenFrame = {
  clientId: string;
  payload: string | number[];
  imageFormat: string;
  width: number;
  height: number;
  sequence: number;
  capturedAtUtc: string;
};

export type AlertItem = {
  studentId: string;
  studentDisplayName: string;
  timestampUtc: string;
  detectionClass: string;
  confidence: number;
  reason: string;
  thumbnailJpegSmall?: string | null;
  modelVersion?: string | null;
  eventId: string;
  stageSource: string;
  isStable: boolean;
  triggeredKeywords: string[];
};

export type PairPinResponse = {
  pinCode: string;
  expiresAtUtc: string;
};

export type UploadInitResponse = {
  transferId: string;
  totalChunks: number;
  createdAtUtc: string;
};

export type AuditItem = {
  id: number;
  timestampUtc: string;
  action: string;
  actor: string;
  details: string;
};

export type DetectionPolicy = TeacherApiSchemas["DetectionPolicyDto"];

export type StudentSignalEvent = {
  studentId: string;
  studentDisplayName: string;
  signalType: "None" | "HandRaise" | string;
  timestampUtc: string;
  eventId: string;
  message?: string | null;
};

export type DetectionExportArtifact = TeacherApiSchemas["DetectionExportArtifactDto"];

export type DetectionExportRequestResult = TeacherApiSchemas["DetectionExportRequestResultDto"];

export type RemoteControlSessionStartResult = {
  accepted: boolean;
  sessionId?: string | null;
  message: string;
};

export type RemoteControlSessionStatus = {
  studentId: string;
  studentDisplayName: string;
  sessionId: string;
  state: "PendingApproval" | "Approved" | "Rejected" | "Ended" | "Expired" | "Error" | string;
  timestampUtc: string;
  message?: string | null;
};

export type RemoteControlInputCommand = {
  clientId: string;
  sessionId: string;
  kind: "MouseMove" | "MouseDown" | "MouseUp" | "MouseWheel" | "KeyDown" | "KeyUp";
  x?: number;
  y?: number;
  button?: "None" | "Left" | "Right" | "Middle";
  wheelDelta?: number;
  key?: string | null;
  code?: string | null;
  ctrl?: boolean;
  alt?: boolean;
  shift?: boolean;
};

export type AccessibilityPresetId = "default" | "vision" | "hearing" | "motor" | "dyslexia" | "custom";
export type AccessibilityContrastMode = "standard" | "aa" | "aaa";
export type AccessibilityColorBlindMode = "none" | "protanopia" | "deuteranopia" | "tritanopia";

export type AccessibilityUiSettingsDto = {
  scalePercent: number;
  contrastMode: AccessibilityContrastMode;
  invertColors: boolean;
  colorBlindMode: AccessibilityColorBlindMode;
  dyslexiaFontEnabled: boolean;
  largeCursorEnabled: boolean;
  highlightFocusEnabled: boolean;
};

export type AccessibilityFeatureFlagsDto = {
  visualAlertsEnabled: boolean;
  largeActionButtonsEnabled: boolean;
  simplifiedNavigationEnabled: boolean;
  singleKeyModeEnabled: boolean;
  ttsTeacherMessagesEnabled: boolean;
  audioLessonModeEnabled: boolean;
  liveCaptionsEnabled: boolean;
  voiceCommandsEnabled: boolean;
};

export type AccessibilityProfileUpdateDto = {
  activePreset: AccessibilityPresetId;
  allowTeacherOverride: boolean;
  ui: AccessibilityUiSettingsDto;
  features: AccessibilityFeatureFlagsDto;
};

export type TeacherStudentChatMessage = {
  clientId: string;
  messageId: string;
  timestampUtc: string;
  senderRole: "teacher" | "student" | string;
  senderDisplayName: string;
  text: string;
};

export type StudentChatHistoryResponse = {
  messages: TeacherStudentChatMessage[];
};

function stringOr(value: unknown, fallback = ""): string {
  return typeof value === "string" ? value : fallback;
}

function dateStringOr(value: unknown, fallback = DEFAULT_DATE_ISO): string {
  return typeof value === "string" && value.trim() ? value : fallback;
}

function numberOr(value: unknown, fallback = 0): number {
  return typeof value === "number" && Number.isFinite(value) ? value : fallback;
}

function booleanOr(value: unknown, fallback = false): boolean {
  return typeof value === "boolean" ? value : fallback;
}

function normalizePreset(value: unknown): AccessibilityPresetId {
  switch (value) {
    case "vision":
    case "hearing":
    case "motor":
    case "dyslexia":
    case "custom":
    case "default":
      return value;
    default:
      return "default";
  }
}

function normalizeContrastMode(value: unknown): AccessibilityContrastMode {
  switch (value) {
    case "aa":
    case "aaa":
    case "standard":
      return value;
    default:
      return "standard";
  }
}

function normalizeColorBlindMode(value: unknown): AccessibilityColorBlindMode {
  switch (value) {
    case "protanopia":
    case "deuteranopia":
    case "tritanopia":
    case "none":
      return value;
    default:
      return "none";
  }
}

export function normalizeAlertItem(value: TeacherApiSchemas["AlertEventDto"] | AlertItem | null | undefined): AlertItem {
  const studentId = stringOr(value?.studentId);
  const timestampUtc = dateStringOr(value?.timestampUtc);
  const detectionClass = stringOr(value?.detectionClass, "UnknownAi");
  return {
    studentId,
    studentDisplayName: stringOr(value?.studentDisplayName),
    timestampUtc,
    detectionClass,
    confidence: numberOr(value?.confidence),
    reason: stringOr(value?.reason),
    thumbnailJpegSmall: value?.thumbnailJpegSmall ?? null,
    modelVersion: value?.modelVersion ?? null,
    eventId: stringOr(value?.eventId, `${studentId || "unknown"}-${timestampUtc}-${detectionClass}`),
    stageSource: stringOr(value?.stageSource),
    isStable: booleanOr(value?.isStable),
    triggeredKeywords: Array.isArray(value?.triggeredKeywords)
      ? value!.triggeredKeywords.filter((item): item is string => typeof item === "string")
      : [],
  };
}

export function normalizePairPinResponse(value: TeacherApiSchemas["PairingPinDto"] | PairPinResponse | null | undefined): PairPinResponse {
  return {
    pinCode: stringOr(value?.pinCode),
    expiresAtUtc: dateStringOr(value?.expiresAtUtc),
  };
}

export function normalizeUploadInitResponse(value: TeacherApiSchemas["FileUploadInitResponse"] | UploadInitResponse | null | undefined): UploadInitResponse {
  return {
    transferId: stringOr(value?.transferId),
    totalChunks: Math.max(0, Math.trunc(numberOr(value?.totalChunks))),
    createdAtUtc: dateStringOr(value?.createdAtUtc),
  };
}

export function normalizeAuditItem(value: TeacherApiSchemas["AuditLogModel"] | AuditItem | null | undefined): AuditItem {
  return {
    id: Math.trunc(numberOr(value?.id)),
    timestampUtc: dateStringOr(value?.timestampUtc),
    action: stringOr(value?.action),
    actor: stringOr(value?.actor),
    details: stringOr(value?.details),
  };
}

export function normalizeAccessibilityProfile(
  value: TeacherApiSchemas["AccessibilityProfileUpdateDto"] | Partial<AccessibilityProfileUpdateDto> | null | undefined,
): AccessibilityProfileUpdateDto {
  return {
    activePreset: normalizePreset(value?.activePreset),
    allowTeacherOverride: booleanOr(value?.allowTeacherOverride, true),
    ui: {
      scalePercent: Math.min(300, Math.max(100, Math.trunc(numberOr(value?.ui?.scalePercent, 100)))),
      contrastMode: normalizeContrastMode(value?.ui?.contrastMode),
      invertColors: booleanOr(value?.ui?.invertColors),
      colorBlindMode: normalizeColorBlindMode(value?.ui?.colorBlindMode),
      dyslexiaFontEnabled: booleanOr(value?.ui?.dyslexiaFontEnabled),
      largeCursorEnabled: booleanOr(value?.ui?.largeCursorEnabled),
      highlightFocusEnabled: booleanOr(value?.ui?.highlightFocusEnabled),
    },
    features: {
      visualAlertsEnabled: booleanOr(value?.features?.visualAlertsEnabled, true),
      largeActionButtonsEnabled: booleanOr(value?.features?.largeActionButtonsEnabled),
      simplifiedNavigationEnabled: booleanOr(value?.features?.simplifiedNavigationEnabled),
      singleKeyModeEnabled: booleanOr(value?.features?.singleKeyModeEnabled),
      ttsTeacherMessagesEnabled: booleanOr(value?.features?.ttsTeacherMessagesEnabled),
      audioLessonModeEnabled: booleanOr(value?.features?.audioLessonModeEnabled),
      liveCaptionsEnabled: booleanOr(value?.features?.liveCaptionsEnabled),
      voiceCommandsEnabled: booleanOr(value?.features?.voiceCommandsEnabled),
    },
  };
}
