export type StatusResponse = {
  hasAdminPassword: boolean;
  isPaired: boolean;
  deviceName: string;
  pairedServerName?: string | null;
  pairedServerBaseUrl?: string | null;
  serverOnline: boolean;
  monitoringActive: boolean;
  agentAutoStart: boolean;
  agentRunning: boolean;
  lastAlert?: string | null;
  detectionEnabled: boolean;
  dataCollectionModeEnabled: boolean;
  lastDetectionCheckUtc?: string | null;
};

export type DiscoveredServer = {
  serverId: string;
  serverName: string;
  host: string;
  port: number;
  baseUrl: string;
  isRecommended: boolean;
};

export type DetectionStatusResponse = {
  detectionEnabled: boolean;
  dataCollectionModeEnabled: boolean;
  lastCheckUtc?: string | null;
  lastResult?: string | null;
  lastModelVersion?: string | null;
  metadataThreshold?: number;
  mlThreshold?: number;
  sampleRate?: number;
  localRetentionDays?: number;
};

export type DiagnosticsExportResponse = {
  archivePath: string;
};

export type AccessibilityPresetId = "default" | "vision" | "hearing" | "motor" | "dyslexia" | "custom";
export type AccessibilityContrastMode = "standard" | "aa" | "aaa";
export type AccessibilityColorBlindMode = "none" | "protanopia" | "deuteranopia" | "tritanopia";

export type AccessibilityUiSettings = {
  scalePercent: number;
  contrastMode: AccessibilityContrastMode;
  invertColors: boolean;
  colorBlindMode: AccessibilityColorBlindMode;
  dyslexiaFontEnabled: boolean;
  largeCursorEnabled: boolean;
  highlightFocusEnabled: boolean;
};

export type AccessibilityFeatureFlags = {
  visualAlertsEnabled: boolean;
  largeActionButtonsEnabled: boolean;
  simplifiedNavigationEnabled: boolean;
  singleKeyModeEnabled: boolean;
  ttsTeacherMessagesEnabled: boolean;
  audioLessonModeEnabled: boolean;
  liveCaptionsEnabled: boolean;
  voiceCommandsEnabled: boolean;
};

export type AccessibilityProfileMetadata = {
  assignmentSource: string;
  assignedBy?: string | null;
  assignedAtUtc?: string | null;
  updatedAtUtc?: string | null;
};

export type AccessibilityProfileResponse = {
  activePreset: AccessibilityPresetId;
  allowTeacherOverride: boolean;
  ui: AccessibilityUiSettings;
  features: AccessibilityFeatureFlags;
  metadata: AccessibilityProfileMetadata;
};

export type AccessibilityProfileUpdateRequest = {
  activePreset: AccessibilityPresetId;
  allowTeacherOverride: boolean;
  ui: AccessibilityUiSettings;
  features: AccessibilityFeatureFlags;
};
