export type Theme = "light" | "dark";

export type FrameState = {
  url: string;
  sequence: number;
  capturedAtUtc: string;
  width: number;
  height: number;
};

export type ProgressPayload = {
  clientId: string;
  error?: string | null;
  completed: boolean;
  completedChunks: number;
  totalChunks: number;
};

export type UiToast = {
  id: string;
  kind: "handRaise" | "aiDetected" | "chatMessage";
  studentName: string;
  detectionClass?: string;
  confidence?: number;
  messageText?: string;
  createdAtMs: number;
};

export type RemoteControlUiSession = {
  sessionId: string;
  state: string;
  message?: string | null;
  updatedAtMs: number;
};

export type TeacherSttTranscribeResponse = {
  ok: boolean;
  text: string;
  language?: string | null;
  task?: string | null;
  duration?: number | null;
  durationAfterVad?: number | null;
};

export type TeacherLiveCaptionRequestDto = {
  text: string;
  isFinal?: boolean;
  clear?: boolean;
  captionId?: string;
  sequence?: number;
  ttlMs?: number;
  languageCode?: string;
  teacherDisplayName?: string;
};

export type AudioInputDevice = {
  deviceId: string;
  label: string;
};

export type TeacherSessionResponse = {
  token: string;
};
