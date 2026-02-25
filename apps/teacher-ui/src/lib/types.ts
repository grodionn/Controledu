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
  detectionClass: string;
  confidence: number;
  reason: string;
  timestampUtc: string;
  thumbnailJpegSmall?: string | number[] | null;
  modelVersion?: string | null;
  eventId: string;
  stageSource: string;
  isStable: boolean;
  triggeredKeywords?: string[] | null;
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

export type DetectionPolicy = {
  enabled: boolean;
  evaluationIntervalSeconds: number;
  frameChangeThreshold: number;
  minRecheckIntervalSeconds: number;
  metadataThreshold: number;
  mlThreshold: number;
  temporalWindowSize: number;
  temporalRequiredVotes: number;
  cooldownSeconds: number;
  keywords: string[];
  whitelistKeywords: string[];
  dataCollectionModeEnabled: boolean;
  dataCollectionMinIntervalSeconds: number;
  dataCollectionSampleRate: number;
  dataCollectionRetentionDays: number;
  dataCollectionStoreFullFrames: boolean;
  dataCollectionStoreThumbnails: boolean;
  includeAlertThumbnails: boolean;
  alertThumbnailWidth: number;
  alertThumbnailHeight: number;
  policyVersion: string;
};

export type StudentSignalEvent = {
  studentId: string;
  studentDisplayName: string;
  signalType: "None" | "HandRaise" | string;
  timestampUtc: string;
  eventId: string;
  message?: string | null;
};

export type DetectionExportArtifact = {
  exportId: string;
  clientId: string;
  studentDisplayName: string;
  createdAtUtc: string;
  fileName: string;
  sizeBytes: number;
  downloadUrl: string;
};

export type DetectionExportRequestResult = {
  requestedCount: number;
  skippedCount: number;
  requestedClientIds: string[];
  skippedClientIds: string[];
};

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
