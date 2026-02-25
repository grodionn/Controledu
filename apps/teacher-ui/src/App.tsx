import { useEffect, useMemo, useRef, useState, type SVGProps } from "react";
import { HubConnection, HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useMutation } from "@tanstack/react-query";
import * as ScrollArea from "@radix-ui/react-scroll-area";
import {
  AccessibilityProfileUpdateDto,
  AlertItem,
  AuditItem,
  DetectionPolicy,
  PairPinResponse,
  RemoteControlInputCommand,
  RemoteControlSessionStartResult,
  RemoteControlSessionStatus,
  ScreenFrame,
  StudentChatHistoryResponse,
  StudentInfo,
  StudentSignalEvent,
  TeacherStudentChatMessage,
  UploadInitResponse,
} from "./lib/types";
import { interpolate, teacherDictionary, UiLanguage } from "./i18n";
import { cn, sha256Hex, toDataUrl } from "./lib/utils";
import { AccessibilityAssignmentCard } from "./components/accessibility-assignment-card";
import { TeacherTtsCard, type TeacherTtsDraft } from "./components/teacher-tts-card";
import { FocusedChatCard } from "./components/focused-chat-card";
import { ThemeToggle } from "./components/theme-toggle";
import { Badge } from "./components/ui/badge";
import { Button } from "./components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "./components/ui/card";
import { Input } from "./components/ui/input";

const CHUNK_SIZE = 256 * 1024;
const THEME_KEY = "controledu.teacher.theme";
const LANG_KEY = "controledu.teacher.lang";
const AI_WARNINGS_KEY = "controledu.teacher.ai-warnings-enabled";
const DESKTOP_NOTIFICATIONS_KEY = "controledu.teacher.desktop-notifications-enabled";
const SOUND_NOTIFICATIONS_KEY = "controledu.teacher.sound-notifications-enabled";
const NOTIFICATION_VOLUME_KEY = "controledu.teacher.notification-volume";
const SELFHOST_TTS_URL_KEY = "controledu.teacher.selfhost-tts-url";
const SELFHOST_TTS_TOKEN_KEY = "controledu.teacher.selfhost-tts-token";
const STT_MIC_DEVICE_ID_KEY = "controledu.teacher.stt-microphone-device-id";
const STT_LANGUAGE_KEY = "controledu.teacher.stt-language";
const THUMBNAIL_FRAME_INTERVAL_MS = 200;
const STT_RECORDER_SLICE_MS = 3600;
const STT_CAPTION_SEND_MIN_INTERVAL_MS = 900;

type Theme = "light" | "dark";

type FrameState = {
  url: string;
  sequence: number;
  capturedAtUtc: string;
  width: number;
  height: number;
};

type ProgressPayload = {
  clientId: string;
  error?: string | null;
  completed: boolean;
  completedChunks: number;
  totalChunks: number;
};

type UiToast = {
  id: string;
  kind: "handRaise" | "aiDetected" | "chatMessage";
  studentName: string;
  detectionClass?: string;
  confidence?: number;
  messageText?: string;
  createdAtMs: number;
};

type RemoteControlUiSession = {
  sessionId: string;
  state: string;
  message?: string | null;
  updatedAtMs: number;
};

type TeacherSttTranscribeResponse = {
  ok: boolean;
  text: string;
  language?: string | null;
  task?: string | null;
  duration?: number | null;
  durationAfterVad?: number | null;
};

type TeacherLiveCaptionRequestDto = {
  text: string;
  isFinal?: boolean;
  clear?: boolean;
  captionId?: string;
  sequence?: number;
  ttlMs?: number;
  languageCode?: string;
  teacherDisplayName?: string;
};

type AudioInputDevice = {
  deviceId: string;
  label: string;
};

async function readResponseText(response: Response): Promise<string> {
  try {
    return await response.text();
  } catch {
    return "";
  }
}

async function fetchJson<T>(input: RequestInfo | URL, init?: RequestInit): Promise<T> {
  const response = await fetch(input, init);
  const bodyText = await readResponseText(response);

  if (!response.ok) {
    throw new Error(bodyText || `Request failed (${response.status}).`);
  }

  const contentType = response.headers.get("content-type") ?? "";
  if (!contentType.includes("application/json")) {
    throw new Error("Endpoint returned non-JSON response.");
  }

  return JSON.parse(bodyText) as T;
}

const fileLikeExtensions = new Set([
  "exe",
  "dll",
  "msi",
  "json",
  "txt",
  "log",
  "png",
  "jpg",
  "jpeg",
  "gif",
  "webp",
  "zip",
  "7z",
  "rar",
  "pdf",
  "doc",
  "docx",
  "xls",
  "xlsx",
]);

function humanizeProgramNames(raw: string): string {
  return raw.replace(/\b[A-Za-z][A-Za-z0-9_-]*(?:\.[A-Za-z][A-Za-z0-9_-]*)+\b/g, (token) => {
    if (/^\d+(?:\.\d+)+$/.test(token)) {
      return token;
    }

    const parts = token.split(".");
    const last = parts[parts.length - 1]?.toLowerCase() ?? "";
    if (fileLikeExtensions.has(last)) {
      return token;
    }

    const looksLikeDomain = parts.length <= 2 && parts.every((part) => part === part.toLowerCase());
    if (looksLikeDomain) {
      return token;
    }

    return parts
      .map((part) => part.replace(/_/g, " ").replace(/([a-z])([A-Z])/g, "$1 $2"))
      .join(" ");
  });
}

type IconProps = SVGProps<SVGSVGElement>;

function IconMonitor(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...props}>
      <rect x="3" y="4" width="18" height="12" rx="2" />
      <path d="M8 20h8" />
      <path d="M12 16v4" />
    </svg>
  );
}

function IconUpload(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...props}>
      <path d="M12 16V6" />
      <path d="m8.5 9.5 3.5-3.5 3.5 3.5" />
      <path d="M4 16.5v1A2.5 2.5 0 0 0 6.5 20h11a2.5 2.5 0 0 0 2.5-2.5v-1" />
    </svg>
  );
}

function IconGrid(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...props}>
      <rect x="3" y="3" width="8" height="8" rx="1.5" />
      <rect x="13" y="3" width="8" height="8" rx="1.5" />
      <rect x="3" y="13" width="8" height="8" rx="1.5" />
      <rect x="13" y="13" width="8" height="8" rx="1.5" />
    </svg>
  );
}

function IconList(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...props}>
      <path d="M9 6h11" />
      <path d="M9 12h11" />
      <path d="M9 18h11" />
      <circle cx="4.5" cy="6" r="1" fill="currentColor" stroke="none" />
      <circle cx="4.5" cy="12" r="1" fill="currentColor" stroke="none" />
      <circle cx="4.5" cy="18" r="1" fill="currentColor" stroke="none" />
    </svg>
  );
}

function IconMic(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...props}>
      <path d="M12 15a3 3 0 0 0 3-3V7a3 3 0 1 0-6 0v5a3 3 0 0 0 3 3Z" />
      <path d="M19 11.5a7 7 0 0 1-14 0" />
      <path d="M12 18.5V21" />
      <path d="M8.5 21h7" />
    </svg>
  );
}

function IconMicOff(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...props}>
      <path d="m3 3 18 18" />
      <path d="M9.2 9.2V12a2.8 2.8 0 0 0 4.73 2.01" />
      <path d="M15 8v4" />
      <path d="M19 11.5a7 7 0 0 1-1.56 4.42" />
      <path d="M5 11.5a7 7 0 0 0 10.08 6.34" />
      <path d="M12 18.5V21" />
      <path d="M8.5 21h7" />
      <path d="M12 4a3 3 0 0 1 2.63 1.56" />
    </svg>
  );
}

function IconChat(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...props}>
      <path d="M7 18 3.5 20v-4.2A8 8 0 1 1 7 18Z" />
      <path d="M8 10h8" />
      <path d="M8 14h5" />
    </svg>
  );
}

function IconVolume(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...props}>
      <path d="M11 5 6 9H3v6h3l5 4V5Z" />
      <path d="M15.5 9.5a4 4 0 0 1 0 5" />
      <path d="M18.5 7a8 8 0 0 1 0 10" />
    </svg>
  );
}

function IconExpand(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...props}>
      <path d="M8 3H3v5" />
      <path d="M16 3h5v5" />
      <path d="M8 21H3v-5" />
      <path d="M16 21h5v-5" />
      <path d="M3 8l5-5" />
      <path d="M21 8l-5-5" />
      <path d="M3 16l5 5" />
      <path d="M21 16l-5 5" />
    </svg>
  );
}

function IconClose(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" {...props}>
      <path d="M18 6 6 18" />
      <path d="m6 6 12 12" />
    </svg>
  );
}

function App() {
  const [theme, setTheme] = useState<Theme>(() => (localStorage.getItem(THEME_KEY) === "light" ? "light" : "dark"));
  const [lang, setLang] = useState<UiLanguage>(() => {
    const value = localStorage.getItem(LANG_KEY);
    return value === "ru" || value === "kz" ? value : "en";
  });
  const [students, setStudents] = useState<Record<string, StudentInfo>>({});
  const [selectedStudentId, setSelectedStudentId] = useState<string | null>(null);
  const [selectedTargets, setSelectedTargets] = useState<Set<string>>(new Set());
  const [frames, setFrames] = useState<Record<string, FrameState>>({});
  const [events, setEvents] = useState<string[]>([]);
  const [pin, setPin] = useState<PairPinResponse | null>(null);
  const [file, setFile] = useState<File | null>(null);
  const [uploadStatus, setUploadStatus] = useState<string>("");
  const [accessibilityAssignStatus, setAccessibilityAssignStatus] = useState<{ tone: "neutral" | "success" | "error"; text: string } | null>(null);
  const [teacherTtsStatus, setTeacherTtsStatus] = useState<{ tone: "neutral" | "success" | "error"; text: string } | null>(null);
  const [teacherChatStatus, setTeacherChatStatus] = useState<{ tone: "neutral" | "success" | "error"; text: string } | null>(null);
  const [teacherSttStatus, setTeacherSttStatus] = useState<{ tone: "neutral" | "success" | "error"; text: string } | null>(null);
  const [chatByStudent, setChatByStudent] = useState<Record<string, TeacherStudentChatMessage[]>>({});
  const [chatUnreadByStudent, setChatUnreadByStudent] = useState<Record<string, number>>({});
  const [chatHistoryLoadingFor, setChatHistoryLoadingFor] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [onlyOnline, setOnlyOnline] = useState(true);
  const [connectionState, setConnectionState] = useState<"connected" | "disconnected">("disconnected");
  const [activeView, setActiveView] = useState<"monitoring" | "chats" | "detection" | "settings">("monitoring");
  const [aiAlerts, setAiAlerts] = useState<AlertItem[]>([]);
  const [detectionPolicy, setDetectionPolicy] = useState<DetectionPolicy | null>(null);
  const [aiFilterStudent, setAiFilterStudent] = useState("all");
  const [aiFilterClass, setAiFilterClass] = useState("all");
  const [aiFilterTime, setAiFilterTime] = useState<"all" | "15m" | "1h" | "24h">("all");
  const [aiWarningsEnabled, setAiWarningsEnabled] = useState(() => localStorage.getItem(AI_WARNINGS_KEY) !== "0");
  const [desktopNotificationsEnabled, setDesktopNotificationsEnabled] = useState(() => localStorage.getItem(DESKTOP_NOTIFICATIONS_KEY) !== "0");
  const [soundNotificationsEnabled, setSoundNotificationsEnabled] = useState(() => localStorage.getItem(SOUND_NOTIFICATIONS_KEY) !== "0");
  const [notificationVolume, setNotificationVolume] = useState(() => {
    const stored = Number(localStorage.getItem(NOTIFICATION_VOLUME_KEY) ?? "72");
    if (Number.isNaN(stored)) {
      return 72;
    }

    return Math.min(100, Math.max(0, Math.round(stored)));
  });
  const [selfHostTtsUrl, setSelfHostTtsUrl] = useState(() => (localStorage.getItem(SELFHOST_TTS_URL_KEY) ?? "https://tts.kilocraft.org").trim() || "https://tts.kilocraft.org");
  const [selfHostTtsToken, setSelfHostTtsToken] = useState(() => localStorage.getItem(SELFHOST_TTS_TOKEN_KEY) ?? "");
  const [sttMicrophoneDeviceId, setSttMicrophoneDeviceId] = useState(() => localStorage.getItem(STT_MIC_DEVICE_ID_KEY) ?? "");
  const [sttLanguageCode, setSttLanguageCode] = useState(() => {
    const stored = (localStorage.getItem(STT_LANGUAGE_KEY) ?? "auto").trim().toLowerCase();
    if (!stored) {
      return "auto";
    }
    return stored === "kz" ? "kk" : stored;
  });
  const [sttAudioInputs, setSttAudioInputs] = useState<AudioInputDevice[]>([]);
  const [sttAudioInputsLoading, setSttAudioInputsLoading] = useState(false);
  const [isLiveSttActive, setIsLiveSttActive] = useState(false);
  const [liveSttTargetClientId, setLiveSttTargetClientId] = useState<string | null>(null);
  const [liveSttPreviewText, setLiveSttPreviewText] = useState("");
  const [handRaisedUntil, setHandRaisedUntil] = useState<Record<string, number>>({});
  const [uiClock, setUiClock] = useState(() => Date.now());
  const [isLogsOpen, setIsLogsOpen] = useState(false);
  const [isTeacherChatModalOpen, setIsTeacherChatModalOpen] = useState(false);
  const [isTeacherTtsModalOpen, setIsTeacherTtsModalOpen] = useState(false);
  const [isSendModalOpen, setIsSendModalOpen] = useState(false);
  const [isThumbnailsExpanded, setIsThumbnailsExpanded] = useState(false);
  const [isRemoteViewOpen, setIsRemoteViewOpen] = useState(false);
  const [remoteControlSessions, setRemoteControlSessions] = useState<Record<string, RemoteControlUiSession>>({});
  const [remoteControlRequestPending, setRemoteControlRequestPending] = useState(false);
  const [toasts, setToasts] = useState<UiToast[]>([]);
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const selectedStudentIdRef = useRef<string | null>(null);
  const activeViewRef = useRef(activeView);
  const frameUpdateGateRef = useRef<Record<string, number>>({});
  const handSignalLogRef = useRef<Record<string, number>>({});
  const toastDedupRef = useRef<Record<string, number>>({});
  const toastTimersRef = useRef<number[]>([]);
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const teacherHubRef = useRef<HubConnection | null>(null);
  const remoteViewportRef = useRef<HTMLDivElement | null>(null);
  const remoteLastPointerSendAtRef = useRef(0);
  const remotePressedKeysRef = useRef<Set<string>>(new Set());
  const aiWarningsEnabledRef = useRef(aiWarningsEnabled);
  const desktopNotificationsEnabledRef = useRef(desktopNotificationsEnabled);
  const soundNotificationsEnabledRef = useRef(soundNotificationsEnabled);
  const notificationVolumeRef = useRef(notificationVolume);
  const sttMediaStreamRef = useRef<MediaStream | null>(null);
  const sttRecorderRef = useRef<MediaRecorder | null>(null);
  const sttRecorderSliceTimerRef = useRef<number | null>(null);
  const sttQueueRef = useRef<Blob[]>([]);
  const sttProcessingRef = useRef(false);
  const sttActiveRef = useRef(false);
  const sttTargetClientIdRef = useRef<string | null>(null);
  const sttCaptionIdRef = useRef<string>("");
  const sttSequenceRef = useRef(0);
  const sttLastCaptionTextRef = useRef("");
  const sttCaptionSegmentsRef = useRef<string[]>([]);
  const sttLastCaptionSentAtRef = useRef(0);

  const t = (key: string) => teacherDictionary[lang][key] ?? key;

  const appendEvent = (line: string) => {
    setEvents((current) => [line, ...current].slice(0, 400));
  };

  const appendChatMessage = (message: TeacherStudentChatMessage) => {
    setChatByStudent((current) => {
      const key = message.clientId;
      const existing = current[key] ?? [];
      if (existing.some((item) => item.messageId === message.messageId)) {
        return current;
      }

      const next = [...existing, message]
        .sort((a, b) => new Date(a.timestampUtc).getTime() - new Date(b.timestampUtc).getTime())
        .slice(-300);
      return { ...current, [key]: next };
    });
  };

  const markChatRead = (clientId: string | null) => {
    if (!clientId) {
      return;
    }

    setChatUnreadByStudent((current) => {
      if (!current[clientId]) {
        return current;
      }

      const next = { ...current };
      delete next[clientId];
      return next;
    });
  };

  const localizeDetectionClass = (value: string): string => {
    switch (value) {
      case "ChatGpt":
        return t("detectionClassChatGpt");
      case "Claude":
        return t("detectionClassClaude");
      case "Gemini":
        return t("detectionClassGemini");
      case "Copilot":
        return t("detectionClassCopilot");
      case "Perplexity":
        return t("detectionClassPerplexity");
      case "DeepSeek":
        return t("detectionClassDeepSeek");
      case "Poe":
        return t("detectionClassPoe");
      case "Grok":
        return t("detectionClassGrok");
      case "Qwen":
        return t("detectionClassQwen");
      case "Mistral":
        return t("detectionClassMistral");
      case "MetaAi":
        return t("detectionClassMetaAi");
      case "UnknownAi":
        return t("detectionClassUnknownAi");
      case "None":
        return t("detectionClassNone");
      default:
        return value;
    }
  };

  const localizeStageSource = (value: string): string => {
    switch (value) {
      case "MetadataRule":
        return t("stageMetadataRule");
      case "OnnxBinary":
        return t("stageOnnxBinary");
      case "OnnxMulticlass":
        return t("stageOnnxMulticlass");
      case "Fused":
        return t("stageFused");
      default:
        return t("stageUnknown");
    }
  };

  const pushToast = (
    toast: Omit<UiToast, "id" | "createdAtMs">,
    dedupeKey: string,
    dedupeMs = 8000,
  ): boolean => {
    const nowMs = Date.now();
    const lastShownMs = toastDedupRef.current[dedupeKey] ?? 0;
    if (nowMs - lastShownMs < dedupeMs) {
      return false;
    }

    toastDedupRef.current[dedupeKey] = nowMs;
    const toastId = `${nowMs}-${Math.random().toString(36).slice(2, 8)}`;

    setToasts((current) => [...current.slice(-2), { ...toast, id: toastId, createdAtMs: nowMs }]);
    const timerId = window.setTimeout(() => {
      setToasts((current) => current.filter((item) => item.id !== toastId));
      toastTimersRef.current = toastTimersRef.current.filter((id) => id !== timerId);
    }, 4500);
    toastTimersRef.current.push(timerId);
    return true;
  };

  const playNotificationSound = () => {
    if (!soundNotificationsEnabledRef.current) {
      return;
    }

    const audio = audioRef.current;
    if (!audio) {
      return;
    }

    audio.pause();
    audio.currentTime = 0;
    audio.volume = Math.min(1, Math.max(0, notificationVolumeRef.current / 100));
    void audio.play().catch(() => undefined);
  };

  const sendDesktopNotification = async (title: string, message: string, kind: "ai" | "signal" | "chat") => {
    if (!desktopNotificationsEnabledRef.current) {
      return;
    }

    if ("Notification" in window && Notification.permission === "granted") {
      try {
        new Notification(title, { body: message });
      } catch {
        // Ignore browser notification failures.
      }
    }

    try {
      await fetch("/api/desktop/notify", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ title, message, kind }),
      });
    } catch {
      // Ignore host notification bridge failures.
    }
  };

  const describeError = (error: unknown) => (error instanceof Error ? error.message : String(error));

  const isLowValueSttFragment = (text: string) => {
    const normalized = text.trim();
    if (!normalized) {
      return true;
    }

    const compact = normalized.replace(/[\s.,!?;:()[\]{}"'`~\\/|@#$%^&*_+=<>-]+/g, "");
    if (compact.length === 0) {
      return true;
    }

    // Drop noisy micro-fragments ("a", "и", "ну", etc.) that flicker in subtitles.
    if (compact.length <= 2 && !/\s/.test(normalized)) {
      return true;
    }

    return false;
  };

  const appendCaptionFragment = (base: string, fragment: string) => {
    const left = base.trim();
    const right = fragment.trim();
    if (!left) {
      return right;
    }
    if (!right) {
      return left;
    }

    const leftLower = left.toLowerCase();
    const rightLower = right.toLowerCase();
    if (leftLower === rightLower || leftLower.endsWith(rightLower)) {
      return left;
    }
    if (rightLower.startsWith(leftLower)) {
      return right;
    }

    let overlap = 0;
    const maxOverlap = Math.min(80, left.length, right.length);
    for (let len = maxOverlap; len >= 4; len--) {
      if (leftLower.slice(-len) === rightLower.slice(0, len)) {
        overlap = len;
        break;
      }
    }

    const suffix = right.slice(overlap).trimStart();
    return suffix ? `${left} ${suffix}` : left;
  };

  const pushSttCaptionSegment = (fragment: string) => {
    const next = fragment.trim();
    if (!next) {
      return "";
    }

    const segments = sttCaptionSegmentsRef.current;
    const last = segments[segments.length - 1];
    if (last) {
      const lastLower = last.toLowerCase();
      const nextLower = next.toLowerCase();
      if (nextLower === lastLower || lastLower.endsWith(nextLower)) {
        return segments.reduce((acc, part) => appendCaptionFragment(acc, part), "");
      }
      if (nextLower.startsWith(lastLower)) {
        segments[segments.length - 1] = next;
      } else {
        segments.push(next);
      }
    } else {
      segments.push(next);
    }

    if (segments.length > 4) {
      segments.splice(0, segments.length - 4);
    }

    let combined = "";
    for (const part of segments) {
      combined = appendCaptionFragment(combined, part);
    }
    combined = combined.replace(/\s+/g, " ").trim();
    if (combined.length > 260) {
      combined = combined.slice(-260).trimStart();
      const firstSpace = combined.indexOf(" ");
      if (firstSpace > 0 && firstSpace < 32) {
        combined = combined.slice(firstSpace + 1);
      }
    }

    return combined;
  };

  const stopLiveStt = async (
    reason = "Live captions stopped.",
    options?: { clearCaption?: boolean; tone?: "neutral" | "success" | "error"; keepStatus?: boolean },
  ) => {
    const clearCaption = options?.clearCaption ?? true;
    const tone = options?.tone ?? "neutral";
    const keepStatus = options?.keepStatus ?? false;

    sttActiveRef.current = false;
    setIsLiveSttActive(false);

    if (sttRecorderSliceTimerRef.current !== null) {
      window.clearTimeout(sttRecorderSliceTimerRef.current);
      sttRecorderSliceTimerRef.current = null;
    }

    const recorder = sttRecorderRef.current;
    sttRecorderRef.current = null;
    if (recorder && recorder.state !== "inactive") {
      try {
        recorder.ondataavailable = null;
        recorder.onerror = null;
        recorder.onstop = null;
        recorder.stop();
      } catch {
        // Ignore recorder stop failures.
      }
    }

    const stream = sttMediaStreamRef.current;
    sttMediaStreamRef.current = null;
    if (stream) {
      stream.getTracks().forEach((track) => {
        try {
          track.stop();
        } catch {
          // Ignore track stop failures.
        }
      });
    }

    sttQueueRef.current = [];
    sttLastCaptionTextRef.current = "";
    sttCaptionSegmentsRef.current = [];
    sttLastCaptionSentAtRef.current = 0;
    sttCaptionIdRef.current = "";
    sttSequenceRef.current = 0;
    setLiveSttPreviewText("");

    const targetClientId = sttTargetClientIdRef.current;
    sttTargetClientIdRef.current = null;
    setLiveSttTargetClientId(null);

    if (clearCaption && targetClientId) {
      try {
        await fetchJson<{ ok: boolean; message?: string }>(`/api/students/${encodeURIComponent(targetClientId)}/live-caption`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            text: "",
            clear: true,
            isFinal: true,
            ttlMs: 1000,
            teacherDisplayName: "Teacher Console",
          } satisfies TeacherLiveCaptionRequestDto),
        });
      } catch {
        // Ignore clear failures during shutdown.
      }
    }

    if (!keepStatus) {
      setTeacherSttStatus({ tone, text: reason });
    }
  };

  const refreshSttMicrophones = async (options?: { requestPermission?: boolean }) => {
    if (typeof navigator === "undefined" || !navigator.mediaDevices?.enumerateDevices) {
      setTeacherSttStatus({ tone: "error", text: "This environment does not support microphone device enumeration." });
      return;
    }

    setSttAudioInputsLoading(true);
    try {
      if (options?.requestPermission) {
        try {
          const probeStream = await navigator.mediaDevices.getUserMedia({ audio: true });
          probeStream.getTracks().forEach((track) => track.stop());
        } catch {
          // Permission may be denied; still try enumerateDevices for device IDs.
        }
      }

      const devices = await navigator.mediaDevices.enumerateDevices();
      const audioInputs = devices
        .filter((device) => device.kind === "audioinput")
        .map((device, index) => ({
          deviceId: device.deviceId,
          label: device.label?.trim() || `Microphone ${index + 1}`,
        }));

      setSttAudioInputs(audioInputs);
      setSttMicrophoneDeviceId((current) => {
        if (!audioInputs.length) {
          return "";
        }
        if (current && audioInputs.some((device) => device.deviceId === current)) {
          return current;
        }
        return audioInputs[0]?.deviceId ?? "";
      });
    } catch (error) {
      setTeacherSttStatus({ tone: "error", text: `Microphone list error: ${describeError(error)}` });
    } finally {
      setSttAudioInputsLoading(false);
    }
  };

  const sendLiveCaptionToStudent = async (clientId: string, payload: TeacherLiveCaptionRequestDto) => {
    return fetchJson<{ ok: boolean; message?: string }>(`/api/students/${encodeURIComponent(clientId)}/live-caption`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });
  };

  const transcribeMicrophoneChunk = async (audioBlob: Blob): Promise<TeacherSttTranscribeResponse> => {
    const formData = new FormData();
    const mimeType = audioBlob.type || "audio/webm";
    const extension = mimeType.includes("ogg") ? "ogg" : mimeType.includes("mp4") || mimeType.includes("m4a") ? "m4a" : "webm";
    formData.append("File", audioBlob, `teacher-mic.${extension}`);
    formData.append("LanguageCode", sttLanguageCode);
    formData.append("Task", "transcribe");
    formData.append("SelfHostBaseUrl", selfHostTtsUrl.trim() || "https://tts.kilocraft.org");
    formData.append("SelfHostApiToken", selfHostTtsToken.trim());
    formData.append("SelfHostSttPath", "/v1/stt/transcribe");

    return fetchJson<TeacherSttTranscribeResponse>("/api/speech/stt/transcribe", {
      method: "POST",
      body: formData,
    });
  };

  const processLiveSttQueue = async () => {
    if (sttProcessingRef.current) {
      return;
    }

    sttProcessingRef.current = true;
    try {
      while (sttActiveRef.current) {
        const targetClientId = sttTargetClientIdRef.current;
        if (!targetClientId) {
          sttQueueRef.current = [];
          return;
        }

        const nextChunk = sttQueueRef.current.shift();
        if (!nextChunk) {
          return;
        }

        try {
          const result = await transcribeMicrophoneChunk(nextChunk);
          const rawText = (result.text ?? "").trim();
          if (!rawText) {
            continue;
          }

          const normalized = rawText.replace(/\s+/g, " ").trim();
          if (!normalized || isLowValueSttFragment(normalized)) {
            continue;
          }

          const combinedCaption = pushSttCaptionSegment(normalized);
          if (!combinedCaption || combinedCaption === sttLastCaptionTextRef.current) {
            continue;
          }

          const nowMs = Date.now();
          if (
            nowMs - sttLastCaptionSentAtRef.current < STT_CAPTION_SEND_MIN_INTERVAL_MS &&
            sttQueueRef.current.length > 0
          ) {
            continue;
          }

          sttLastCaptionSentAtRef.current = nowMs;
          sttLastCaptionTextRef.current = combinedCaption;
          setLiveSttPreviewText(combinedCaption);
          sttSequenceRef.current += 1;

          await sendLiveCaptionToStudent(targetClientId, {
            text: combinedCaption,
            isFinal: true,
            clear: false,
            captionId: sttCaptionIdRef.current,
            sequence: sttSequenceRef.current,
            ttlMs: 7000,
            languageCode: (result.language ?? sttLanguageCode) || undefined,
            teacherDisplayName: "Teacher Console",
          });

          setTeacherSttStatus({
            tone: "success",
            text: `Live captions: ${combinedCaption.slice(0, 90)}${combinedCaption.length > 90 ? "..." : ""}`,
          });
        } catch (error) {
          const message = `STT error: ${describeError(error)}`;
          setTeacherSttStatus({ tone: "error", text: message });
          appendEvent(message);
        }
      }
    } finally {
      sttProcessingRef.current = false;
    }
  };

  const startLiveSttRecorderSlice = (stream: MediaStream, preferredMimeType: string) => {
    if (!sttActiveRef.current || !stream.active) {
      return;
    }

    let recorder: MediaRecorder;
    try {
      recorder = preferredMimeType ? new MediaRecorder(stream, { mimeType: preferredMimeType }) : new MediaRecorder(stream);
    } catch {
      recorder = new MediaRecorder(stream);
    }

    const chunks: Blob[] = [];
    recorder.ondataavailable = (event: BlobEvent) => {
      const blob = event.data;
      if (blob && blob.size > 0) {
        chunks.push(blob);
      }
    };

    recorder.onerror = (event) => {
      const err = (event as unknown as { error?: { message?: string } }).error?.message ?? "Recorder error";
      void stopLiveStt(`Live captions stopped: ${err}`, { tone: "error" });
    };

    recorder.onstop = () => {
      if (sttRecorderRef.current === recorder) {
        sttRecorderRef.current = null;
      }

      if (sttRecorderSliceTimerRef.current !== null) {
        window.clearTimeout(sttRecorderSliceTimerRef.current);
        sttRecorderSliceTimerRef.current = null;
      }

      if (chunks.length > 0 && sttActiveRef.current) {
        const mimeType = recorder.mimeType || preferredMimeType || chunks[0]?.type || "audio/webm";
        const blob = new Blob(chunks, { type: mimeType });
        sttQueueRef.current.push(blob);
        if (sttQueueRef.current.length > 2) {
          sttQueueRef.current.splice(0, sttQueueRef.current.length - 2);
        }
        void processLiveSttQueue();
      }

      if (!sttActiveRef.current || sttMediaStreamRef.current !== stream || !stream.active) {
        return;
      }

      window.setTimeout(() => {
        if (sttActiveRef.current && sttMediaStreamRef.current === stream && stream.active) {
          startLiveSttRecorderSlice(stream, preferredMimeType);
        }
      }, 0);
    };

    sttRecorderRef.current = recorder;
    recorder.start();
    sttRecorderSliceTimerRef.current = window.setTimeout(() => {
      if (!sttActiveRef.current) {
        return;
      }
      if (sttRecorderRef.current !== recorder) {
        return;
      }
      if (recorder.state !== "inactive") {
        try {
          recorder.stop();
        } catch {
          // Ignore stop race conditions.
        }
      }
    }, STT_RECORDER_SLICE_MS);
  };

  const startLiveStt = async () => {
    if (isLiveSttActive || sttActiveRef.current) {
      return;
    }

    if (typeof window === "undefined" || typeof navigator === "undefined") {
      setTeacherSttStatus({ tone: "error", text: "Microphone capture is not available in this environment." });
      return;
    }

    if (!("MediaRecorder" in window)) {
      setTeacherSttStatus({ tone: "error", text: "MediaRecorder is not supported in this environment." });
      return;
    }

    if (!navigator.mediaDevices?.getUserMedia) {
      setTeacherSttStatus({ tone: "error", text: "Microphone API is not available." });
      return;
    }

    if (!selectedStudentId || !students[selectedStudentId]?.isOnline) {
      setTeacherSttStatus({ tone: "error", text: "Select an online student to start live captions." });
      return;
    }

    if (!selfHostTtsToken.trim()) {
      setTeacherSttStatus({ tone: "error", text: "Configure the self-host speech token in Settings before using STT." });
      return;
    }

    try {
      setTeacherSttStatus({ tone: "neutral", text: `Starting live captions for ${selectedStudentId}...` });

      const audioConstraints: MediaTrackConstraints =
        sttMicrophoneDeviceId && sttMicrophoneDeviceId !== "default"
          ? {
              deviceId: { exact: sttMicrophoneDeviceId },
              echoCancellation: true,
              noiseSuppression: true,
              autoGainControl: true,
            }
          : {
              echoCancellation: true,
              noiseSuppression: true,
              autoGainControl: true,
            };

      const stream = await navigator.mediaDevices.getUserMedia({ audio: audioConstraints });
      sttMediaStreamRef.current = stream;

      const mimeCandidates = ["audio/webm;codecs=opus", "audio/webm", "audio/ogg;codecs=opus"];
      const selectedMimeType =
        typeof MediaRecorder !== "undefined" && typeof MediaRecorder.isTypeSupported === "function"
          ? (mimeCandidates.find((value) => MediaRecorder.isTypeSupported(value)) ?? "")
          : "";
      sttActiveRef.current = true;
      setIsLiveSttActive(true);
      sttTargetClientIdRef.current = selectedStudentId;
      setLiveSttTargetClientId(selectedStudentId);
      sttCaptionIdRef.current = `live-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
      sttSequenceRef.current = 0;
      sttLastCaptionTextRef.current = "";
      sttCaptionSegmentsRef.current = [];
      sttLastCaptionSentAtRef.current = 0;
      sttQueueRef.current = [];
      setLiveSttPreviewText("");
      startLiveSttRecorderSlice(stream, selectedMimeType);

      try {
        await refreshSttMicrophones();
      } catch {
        // Device labels refresh is best effort.
      }

      await sendLiveCaptionToStudent(selectedStudentId, {
        text: "",
        clear: true,
        isFinal: true,
        captionId: sttCaptionIdRef.current,
        ttlMs: 1000,
        teacherDisplayName: "Teacher Console",
      });

      appendEvent(`Live captions started for ${selectedStudentId}`);
      setTeacherSttStatus({ tone: "success", text: `Live captions enabled for ${selectedStudentId}.` });
    } catch (error) {
      await stopLiveStt(`Live captions failed to start: ${describeError(error)}`, { clearCaption: false, tone: "error" });
    }
  };

  useEffect(() => {
    document.documentElement.classList.toggle("dark", theme === "dark");
    localStorage.setItem(THEME_KEY, theme);
  }, [theme]);

  useEffect(() => {
    localStorage.setItem(LANG_KEY, lang);
  }, [lang]);

  useEffect(() => {
    aiWarningsEnabledRef.current = aiWarningsEnabled;
    localStorage.setItem(AI_WARNINGS_KEY, aiWarningsEnabled ? "1" : "0");
  }, [aiWarningsEnabled]);

  useEffect(() => {
    desktopNotificationsEnabledRef.current = desktopNotificationsEnabled;
    localStorage.setItem(DESKTOP_NOTIFICATIONS_KEY, desktopNotificationsEnabled ? "1" : "0");
  }, [desktopNotificationsEnabled]);

  useEffect(() => {
    soundNotificationsEnabledRef.current = soundNotificationsEnabled;
    localStorage.setItem(SOUND_NOTIFICATIONS_KEY, soundNotificationsEnabled ? "1" : "0");
  }, [soundNotificationsEnabled]);

  useEffect(() => {
    notificationVolumeRef.current = notificationVolume;
    localStorage.setItem(NOTIFICATION_VOLUME_KEY, String(notificationVolume));
  }, [notificationVolume]);

  useEffect(() => {
    localStorage.setItem(SELFHOST_TTS_URL_KEY, selfHostTtsUrl.trim() || "https://tts.kilocraft.org");
  }, [selfHostTtsUrl]);

  useEffect(() => {
    localStorage.setItem(SELFHOST_TTS_TOKEN_KEY, selfHostTtsToken);
  }, [selfHostTtsToken]);

  useEffect(() => {
    localStorage.setItem(STT_MIC_DEVICE_ID_KEY, sttMicrophoneDeviceId);
  }, [sttMicrophoneDeviceId]);

  useEffect(() => {
    localStorage.setItem(STT_LANGUAGE_KEY, sttLanguageCode);
  }, [sttLanguageCode]);

  useEffect(() => {
    void refreshSttMicrophones();

    const mediaDevices = typeof navigator !== "undefined" ? navigator.mediaDevices : undefined;
    if (!mediaDevices) {
      return;
    }

    const handleDeviceChange = () => {
      void refreshSttMicrophones();
    };

    if (typeof mediaDevices.addEventListener === "function") {
      mediaDevices.addEventListener("devicechange", handleDeviceChange);
      return () => mediaDevices.removeEventListener("devicechange", handleDeviceChange);
    }

    mediaDevices.ondevicechange = handleDeviceChange;
    return () => {
      if (mediaDevices.ondevicechange === handleDeviceChange) {
        mediaDevices.ondevicechange = null;
      }
    };
  }, []);

  useEffect(() => {
    const audio = new Audio("/notification.mp3");
    audio.preload = "auto";
    audio.volume = Math.min(1, Math.max(0, notificationVolume / 100));
    audioRef.current = audio;

    return () => {
      audio.pause();
      audioRef.current = null;
    };
  }, []);

  useEffect(() => {
    selectedStudentIdRef.current = selectedStudentId;
  }, [selectedStudentId]);

  useEffect(() => {
    activeViewRef.current = activeView;
  }, [activeView]);

  useEffect(() => {
    if (activeView === "chats" && selectedStudentId) {
      markChatRead(selectedStudentId);
    }
  }, [activeView, selectedStudentId]);

  useEffect(() => {
    const timer = window.setInterval(() => setUiClock(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    return () => {
      toastTimersRef.current.forEach((timerId) => window.clearTimeout(timerId));
      toastTimersRef.current = [];
    };
  }, []);

  useEffect(() => {
    return () => {
      void stopLiveStt("Live captions stopped.", { clearCaption: true, keepStatus: true });
    };
  }, []);

  useEffect(() => {
    if (!isLiveSttActive) {
      return;
    }

    const targetClientId = sttTargetClientIdRef.current;
    if (!targetClientId) {
      return;
    }

    const targetStudent = students[targetClientId];
    if (!targetStudent || !targetStudent.isOnline) {
      void stopLiveStt("Live captions stopped: target device went offline.", { tone: "error" });
      return;
    }

    if (selectedStudentId !== targetClientId) {
      void stopLiveStt("Live captions stopped: selected device changed.", { tone: "neutral" });
    }
  }, [isLiveSttActive, selectedStudentId, students]);

  useEffect(() => {
    let connection: HubConnection | null = null;
    let cancelled = false;

    const setup = async () => {
      connection = new HubConnectionBuilder()
        .withUrl("/hubs/teacher")
        .withAutomaticReconnect([0, 1000, 3000, 7000, 15000])
        .configureLogging(LogLevel.Warning)
        .build();
      teacherHubRef.current = connection;

      connection.onreconnected(() => setConnectionState("connected"));
      connection.onclose(() => setConnectionState("disconnected"));

      connection.on("StudentListChanged", (list: StudentInfo[]) => {
        const next = Object.fromEntries(list.map((item) => [item.clientId, item]));
        setStudents(next);
        setSelectedTargets((current) => new Set([...current].filter((clientId) => Boolean(next[clientId]))));
        setSelectedStudentId((current) => {
          if (current && next[current]) {
            return current;
          }

          if (list.length === 0) {
            return null;
          }

          const firstOnline = list.find((x) => x.isOnline) ?? list[0];
          return firstOnline.clientId;
        });
      });

      connection.on("StudentUpserted", (student: StudentInfo) => {
        setStudents((current) => ({ ...current, [student.clientId]: student }));
      });

      connection.on("StudentDisconnected", (clientId: string) => {
        setStudents((current) => {
          const existing = current[clientId];
          if (!existing) {
            return current;
          }

          return { ...current, [clientId]: { ...existing, isOnline: false } };
        });
      });

      connection.on("FrameReceived", (frame: ScreenFrame) => {
        const nowMs = Date.now();
        const isFocused = selectedStudentIdRef.current === frame.clientId;
        if (!isFocused) {
          const lastUpdate = frameUpdateGateRef.current[frame.clientId] ?? 0;
          if (nowMs - lastUpdate < THUMBNAIL_FRAME_INTERVAL_MS) {
            return;
          }

          frameUpdateGateRef.current[frame.clientId] = nowMs;
        }

        setFrames((current) => {
          const previous = current[frame.clientId];
          if (previous && previous.sequence >= frame.sequence) {
            return current;
          }

          return {
            ...current,
            [frame.clientId]: {
              url: toDataUrl(frame.payload),
              sequence: frame.sequence,
              capturedAtUtc: frame.capturedAtUtc,
              width: frame.width,
              height: frame.height,
            },
          };
        });
      });

      connection.on("AlertReceived", (alert: AlertItem) => {
        setAiAlerts((current) => [alert, ...current].slice(0, 1000));
        appendEvent(`[${new Date(alert.timestampUtc).toLocaleTimeString()}] ALERT ${alert.studentId}: ${alert.detectionClass} ${alert.confidence.toFixed(2)} - ${alert.reason}`);
        if (!aiWarningsEnabledRef.current) {
          return;
        }

        const shown = pushToast(
          {
            kind: "aiDetected",
            studentName: alert.studentDisplayName || alert.studentId,
            detectionClass: alert.detectionClass,
            confidence: alert.confidence,
          },
          `ai:${alert.studentId}:${alert.detectionClass}`,
          10000,
        );

        if (shown) {
          const title = t("toastAlertTitle");
          const message = interpolate(t("toastAiDetected"), {
            name: alert.studentDisplayName || alert.studentId,
            className: localizeDetectionClass(alert.detectionClass),
            confidence: alert.confidence.toFixed(2),
          });
          playNotificationSound();
          void sendDesktopNotification(title, message, "ai");
        }
      });

      connection.on("StudentSignalReceived", (signal: StudentSignalEvent) => {
        if (signal.signalType !== "HandRaise") {
          return;
        }

        const nowMs = Date.now();
        setHandRaisedUntil((current) => ({
          ...current,
          [signal.studentId]: nowMs + 45_000,
        }));

        const lastLoggedAt = handSignalLogRef.current[signal.studentId] ?? 0;
        if (nowMs - lastLoggedAt >= 10_000) {
          appendEvent(`[${new Date(signal.timestampUtc).toLocaleTimeString()}] ${signal.studentDisplayName}: Hand raise`);
          handSignalLogRef.current[signal.studentId] = nowMs;
        }

        const shown = pushToast(
          {
            kind: "handRaise",
            studentName: signal.studentDisplayName || signal.studentId,
          },
          `hand:${signal.studentId}`,
          10000,
        );

        if (shown) {
          const title = t("toastSignalTitle");
          const message = interpolate(t("toastHandRaise"), { name: signal.studentDisplayName || signal.studentId });
          playNotificationSound();
          void sendDesktopNotification(title, message, "signal");
        }
      });

      connection.on("ChatMessageReceived", (message: TeacherStudentChatMessage) => {
        appendChatMessage(message);
        const author = message.senderDisplayName || (String(message.senderRole).toLowerCase() === "teacher" ? "Teacher" : message.clientId);
        appendEvent(`[${new Date(message.timestampUtc).toLocaleTimeString()}] CHAT ${message.clientId} ${author}: ${message.text.slice(0, 80)}`);

        const isStudentMessage = String(message.senderRole).toLowerCase() === "student";
        if (!isStudentMessage) {
          return;
        }

        const isVisibleInChats =
          activeViewRef.current === "chats" && selectedStudentIdRef.current === message.clientId;
        if (!isVisibleInChats) {
          setChatUnreadByStudent((current) => ({
            ...current,
            [message.clientId]: (current[message.clientId] ?? 0) + 1,
          }));
        }

        const preview = message.text.trim().replace(/\s+/g, " ").slice(0, 90);
        const studentName = message.senderDisplayName || message.clientId;
        const shown = pushToast(
          {
            kind: "chatMessage",
            studentName,
            messageText: preview,
          },
          `chat:${message.clientId}:${preview.slice(0, 32)}`,
          3000,
        );

        if (shown) {
          const title = t("toastChatTitle");
          const text = interpolate(t("toastChatReceived"), { name: studentName, text: preview || "..." });
          playNotificationSound();
          void sendDesktopNotification(title, text, "chat");
        }
      });

      connection.on("DetectionPolicyUpdated", (policy: DetectionPolicy) => {
        setDetectionPolicy(policy);
      });

      connection.on("FileProgressUpdated", (progress: ProgressPayload) => {
        if (progress.clientId === "server") {
          setUploadStatus(progress.error ?? "Dispatch created");
          return;
        }

        const message = progress.completed
          ? `Delivery completed for ${progress.clientId}`
          : `Delivery ${progress.clientId}: ${progress.completedChunks}/${progress.totalChunks}`;
        appendEvent(message);
      });

      connection.on("RemoteControlStatusUpdated", (status: RemoteControlSessionStatus) => {
        setRemoteControlSessions((current) => ({
          ...current,
          [status.studentId]: {
            sessionId: status.sessionId,
            state: status.state,
            message: status.message,
            updatedAtMs: new Date(status.timestampUtc).getTime(),
          },
        }));
        setRemoteControlRequestPending(false);
        appendEvent(
          `[${new Date(status.timestampUtc).toLocaleTimeString()}] REMOTE ${status.studentId}: ${status.state}${status.message ? ` - ${status.message}` : ""}`,
        );
      });

      await connection.start();
      setConnectionState("connected");

      try {
        const audit = await fetchJson<AuditItem[]>("/api/audit/latest?take=120");
        const detectionEvents = await fetchJson<AlertItem[]>("/api/detection/events?take=200");
        const policy = await fetchJson<DetectionPolicy>("/api/detection/settings");
        if (!cancelled) {
          setAiAlerts(detectionEvents);
          setDetectionPolicy(policy);
          setEvents((current) => [
            ...audit.map((x) => `[${new Date(x.timestampUtc).toLocaleTimeString()}] ${x.action} (${x.actor}) ${x.details}`),
            ...current,
          ].slice(0, 400));
        }
      } catch (error) {
        appendEvent(`Audit preload failed: ${describeError(error)}`);
      }
    };

    setup().catch((error) => {
      setConnectionState("disconnected");
      appendEvent(`Connection failed: ${describeError(error)}`);
    });

    return () => {
      cancelled = true;
      teacherHubRef.current = null;
      if (connection) {
        connection.stop().catch(() => undefined);
      }
    };
  }, []);

  const sortedStudents = useMemo(() => {
    const values = Object.values(students);
    return values
      .filter((student) => (!onlyOnline || student.isOnline))
      .filter((student) => {
        if (!search.trim()) {
          return true;
        }

        const target = `${student.hostName} ${student.userName} ${student.localIpAddress ?? ""}`.toLowerCase();
        return target.includes(search.trim().toLowerCase());
      })
      .sort((a, b) => {
        if (a.isOnline !== b.isOnline) {
          return a.isOnline ? -1 : 1;
        }

        return a.hostName.localeCompare(b.hostName);
      });
  }, [students, onlyOnline, search]);

  const selectedStudent = selectedStudentId ? students[selectedStudentId] : undefined;
  const selectedFrame = selectedStudentId ? frames[selectedStudentId] : undefined;

  const onlineStudents = useMemo(
    () => Object.values(students).filter((student) => student.isOnline),
    [students],
  );

  useEffect(() => {
    if (!selectedStudentId) {
      setTeacherChatStatus(null);
      setChatHistoryLoadingFor(null);
      return;
    }

    let cancelled = false;
    setChatHistoryLoadingFor(selectedStudentId);

    fetchJson<StudentChatHistoryResponse>(`/api/students/${encodeURIComponent(selectedStudentId)}/chat?take=120`)
      .then((response) => {
        if (cancelled) {
          return;
        }

        setChatByStudent((current) => ({
          ...current,
          [selectedStudentId]: [...(response.messages ?? [])].sort(
            (a, b) => new Date(a.timestampUtc).getTime() - new Date(b.timestampUtc).getTime(),
          ),
        }));
      })
      .catch((error) => {
        if (!cancelled) {
          setTeacherChatStatus({ tone: "error", text: `Chat history error: ${describeError(error)}` });
        }
      })
      .finally(() => {
        if (!cancelled) {
          setChatHistoryLoadingFor((current) => (current === selectedStudentId ? null : current));
        }
      });

    return () => {
      cancelled = true;
    };
  }, [selectedStudentId]);

  const filteredAiAlerts = useMemo(() => {
    const now = Date.now();
    return aiAlerts.filter((alert) => {
      if (aiFilterStudent !== "all" && alert.studentId !== aiFilterStudent) {
        return false;
      }

      if (aiFilterClass !== "all" && alert.detectionClass !== aiFilterClass) {
        return false;
      }

      if (aiFilterTime !== "all") {
        const ageMs = now - new Date(alert.timestampUtc).getTime();
        const limitMs =
          aiFilterTime === "15m"
            ? 15 * 60 * 1000
            : aiFilterTime === "1h"
              ? 60 * 60 * 1000
              : 24 * 60 * 60 * 1000;
        if (ageMs > limitMs) {
          return false;
        }
      }

      return true;
    });
  }, [aiAlerts, aiFilterStudent, aiFilterClass, aiFilterTime]);

  const generatePin = useMutation({
    mutationFn: async (): Promise<PairPinResponse> => fetchJson<PairPinResponse>("/api/pairing/pin", { method: "POST" }),
    onSuccess: (value) => {
      setPin(value);
      appendEvent(`New pairing PIN generated until ${new Date(value.expiresAtUtc).toLocaleTimeString()}`);
    },
    onError: (error) => appendEvent(`PIN error: ${describeError(error)}`),
  });

  const sendFile = useMutation({
    mutationFn: async (targets: string[]) => {
      if (!file) {
        throw new Error("Choose a file first.");
      }

      if (targets.length === 0) {
        throw new Error("No target devices selected.");
      }

      setUploadStatus("Preparing upload...");
      const bytes = new Uint8Array(await file.arrayBuffer());
      const fileHash = await sha256Hex(bytes);

      const init = await fetchJson<UploadInitResponse>("/api/files/upload/init", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          fileName: file.name,
          fileSize: file.size,
          sha256: fileHash,
          chunkSize: CHUNK_SIZE,
          uploadedBy: "teacher-ui",
        }),
      });

      for (let index = 0; index < init.totalChunks; index++) {
        const chunk = bytes.slice(index * CHUNK_SIZE, Math.min((index + 1) * CHUNK_SIZE, bytes.length));
        const chunkHash = await sha256Hex(chunk);

        const uploadChunk = await fetch(`/api/files/upload/${init.transferId}/chunk/${index}`, {
          method: "PUT",
          headers: {
            "Content-Type": "application/octet-stream",
            "X-Chunk-Sha256": chunkHash,
          },
          body: chunk,
        });

        if (!uploadChunk.ok) {
          throw new Error(`Chunk ${index + 1} upload failed`);
        }

        setUploadStatus(`Uploaded ${index + 1}/${init.totalChunks} chunks`);
      }

      const dispatchResponse = await fetch(`/api/files/${init.transferId}/dispatch`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ transferId: init.transferId, targetClientIds: targets }),
      });

      if (!dispatchResponse.ok) {
        throw new Error(await dispatchResponse.text());
      }

      setUploadStatus(`Dispatch created for ${targets.length} device(s)`);
    },
    onError: (error) => {
      const message = describeError(error);
      setUploadStatus(message);
      appendEvent(`File transfer error: ${message}`);
    },
  });

  const removeStudent = useMutation({
    mutationFn: async (clientId: string) => {
      const response = await fetch(`/api/students/${encodeURIComponent(clientId)}`, { method: "DELETE" });
      if (!response.ok) {
        const body = await response.text();
        throw new Error(body || "Failed to remove device.");
      }
    },
    onSuccess: (_, clientId) => {
      appendEvent(`Device removed: ${clientId}`);
      setSelectedTargets((current) => {
        const next = new Set(current);
        next.delete(clientId);
        return next;
      });
      setSelectedStudentId((current) => (current === clientId ? null : current));
    },
    onError: (error) => appendEvent(`Remove device error: ${describeError(error)}`),
  });

  const assignAccessibilityProfile = useMutation({
    mutationFn: async ({ clientId, profile }: { clientId: string; profile: AccessibilityProfileUpdateDto }) => {
      return fetchJson<{ ok: boolean; message?: string }>(`/api/students/${encodeURIComponent(clientId)}/accessibility-profile`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          teacherDisplayName: "Teacher Console",
          profile,
        }),
      });
    },
    onSuccess: (_, vars) => {
      const message = `Accessibility profile sent to ${vars.clientId} (${vars.profile.activePreset})`;
      setAccessibilityAssignStatus({ tone: "success", text: message });
      appendEvent(message);
    },
    onError: (error) => {
      const message = `Accessibility profile error: ${describeError(error)}`;
      setAccessibilityAssignStatus({ tone: "error", text: message });
      appendEvent(message);
    },
  });

  const sendTeacherTts = useMutation({
    mutationFn: async ({ clientId, draft }: { clientId: string; draft: TeacherTtsDraft }) => {
      return fetchJson<{ ok: boolean; message?: string }>(`/api/students/${encodeURIComponent(clientId)}/tts`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          teacherDisplayName: "Teacher Console",
          messageText: draft.messageText,
          languageCode: draft.languageCode,
          voiceName: draft.voiceName,
          speakingRate: draft.speakingRate,
          pitch: draft.pitch,
          selfHostBaseUrl: selfHostTtsUrl.trim() || undefined,
          selfHostApiToken: selfHostTtsToken.trim() || undefined,
          selfHostTtsPath: "/v1/tts/synthesize",
        }),
      });
    },
    onSuccess: (result, vars) => {
      const base = result.message?.trim() ? result.message : "Teacher TTS sent";
      const message = `${base} (${vars.clientId})`;
      setTeacherTtsStatus({ tone: "success", text: message });
      appendEvent(`${message}: ${vars.draft.messageText.slice(0, 60)}`);
    },
    onError: (error) => {
      const message = `Teacher TTS error: ${describeError(error)}`;
      setTeacherTtsStatus({ tone: "error", text: message });
      appendEvent(message);
    },
  });

  const sendTeacherChat = useMutation({
    mutationFn: async ({ clientId, text }: { clientId: string; text: string }) => {
      return fetchJson<{ ok: boolean; message?: string; chat?: TeacherStudentChatMessage }>(`/api/students/${encodeURIComponent(clientId)}/chat`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          teacherDisplayName: "Teacher Console",
          text,
        }),
      });
    },
    onSuccess: (result, vars) => {
      if (result.chat) {
        appendChatMessage(result.chat);
      }

      const message = `Teacher chat sent to ${vars.clientId}`;
      setTeacherChatStatus({ tone: "success", text: message });
      appendEvent(`${message}: ${vars.text.slice(0, 80)}`);
    },
    onError: (error) => {
      const message = `Teacher chat error: ${describeError(error)}`;
      setTeacherChatStatus({ tone: "error", text: message });
      appendEvent(message);
    },
  });

  const toggleTarget = (clientId: string) => {
    setSelectedTargets((current) => {
      const next = new Set(current);
      if (next.has(clientId)) {
        next.delete(clientId);
      } else {
        next.add(clientId);
      }
      return next;
    });
  };

  const sendToAll = () => {
    const targets = onlineStudents.map((student) => student.clientId);
    sendFile.mutate(targets);
  };

  const sendToSelected = () => {
    const targets = [...selectedTargets];
    if (targets.length === 0 && selectedStudentId) {
      sendFile.mutate([selectedStudentId]);
      return;
    }

    sendFile.mutate(targets);
  };

  const openFileDialog = () => fileInputRef.current?.click();

  const selectedTargetCount = selectedTargets.size || (selectedStudentId ? 1 : 0);
  const canOpenRemoteView = Boolean(selectedStudent);
  const selectedChatMessages = selectedStudentId ? (chatByStudent[selectedStudentId] ?? []) : [];
  const unreadChatTotal = Object.values(chatUnreadByStudent).reduce((sum, count) => sum + count, 0);
  const activeLiveSttTarget = liveSttTargetClientId ? students[liveSttTargetClientId] : undefined;
  const sttSelectedLanguageLabel =
    sttLanguageCode === "auto"
      ? "Auto"
      : sttLanguageCode === "ru"
        ? "Русский"
        : sttLanguageCode === "en"
          ? "English"
          : sttLanguageCode === "kk" || sttLanguageCode === "kz"
            ? "Қазақша"
            : sttLanguageCode;
  const selectedRemoteControlSession = selectedStudentId ? remoteControlSessions[selectedStudentId] : undefined;
  const isRemoteControlApproved = selectedRemoteControlSession?.state === "Approved";
  const isRemoteControlPending =
    remoteControlRequestPending || selectedRemoteControlSession?.state === "PendingApproval";

  const handleFocusedTeacherChatSend = (text: string) => {
    if (!selectedStudentId) {
      setTeacherChatStatus({ tone: "error", text: "Select a device first." });
      return;
    }

    setTeacherChatStatus({ tone: "neutral", text: `Sending chat to ${selectedStudentId}...` });
    sendTeacherChat.mutate({ clientId: selectedStudentId, text });
  };

  const handleTeacherTtsSend = (draft: TeacherTtsDraft) => {
    if (!selectedStudentId) {
      setTeacherTtsStatus({ tone: "error", text: "Select a device first." });
      return;
    }

    setTeacherTtsStatus({ tone: "neutral", text: `Dispatching TTS to ${selectedStudentId}...` });
    sendTeacherTts.mutate({ clientId: selectedStudentId, draft });
  };

  useEffect(() => {
    if (activeView !== "chats") {
      return;
    }

    if (selectedStudentId && students[selectedStudentId]) {
      return;
    }

    const firstAvailable = sortedStudents.find((student) => student.isOnline) ?? sortedStudents[0];
    if (!firstAvailable) {
      return;
    }

    setSelectedStudentId(firstAvailable.clientId);
    markChatRead(firstAvailable.clientId);
  }, [activeView, selectedStudentId, sortedStudents, students]);

  const requestRemoteControlSession = async () => {
    if (!selectedStudentId || !teacherHubRef.current) {
      return;
    }

    setRemoteControlRequestPending(true);
    try {
      const result = await teacherHubRef.current.invoke<RemoteControlSessionStartResult>(
        "RequestRemoteControlSession",
        selectedStudentId,
      );
      if (!result.accepted || !result.sessionId) {
        appendEvent(`Remote control request rejected: ${result.message}`);
        setRemoteControlRequestPending(false);
        return;
      }

      setRemoteControlSessions((current) => ({
        ...current,
        [selectedStudentId]: {
          sessionId: result.sessionId!,
          state: "PendingApproval",
          message: result.message,
          updatedAtMs: Date.now(),
        },
      }));
      appendEvent(`Remote control requested for ${selectedStudentId}`);
    } catch (error) {
      setRemoteControlRequestPending(false);
      appendEvent(`Remote control request failed: ${describeError(error)}`);
    }
  };

  const stopRemoteControlSession = async () => {
    if (!selectedStudentId || !teacherHubRef.current) {
      return;
    }

    try {
      await teacherHubRef.current.invoke("StopRemoteControlSession", selectedStudentId);
      appendEvent(`Remote control stop requested for ${selectedStudentId}`);
    } catch (error) {
      appendEvent(`Remote control stop failed: ${describeError(error)}`);
    }
  };

  const getRemotePoint = (clientX: number, clientY: number) => {
    const viewport = remoteViewportRef.current;
    if (!viewport || !selectedFrame || selectedFrame.width <= 0 || selectedFrame.height <= 0) {
      return null;
    }

    const rect = viewport.getBoundingClientRect();
    if (rect.width <= 2 || rect.height <= 2) {
      return null;
    }

    const frameAspect = selectedFrame.width / selectedFrame.height;
    const boxAspect = rect.width / rect.height;

    let renderWidth = rect.width;
    let renderHeight = rect.height;
    let offsetX = 0;
    let offsetY = 0;

    if (boxAspect > frameAspect) {
      renderHeight = rect.height;
      renderWidth = renderHeight * frameAspect;
      offsetX = (rect.width - renderWidth) / 2;
    } else {
      renderWidth = rect.width;
      renderHeight = renderWidth / frameAspect;
      offsetY = (rect.height - renderHeight) / 2;
    }

    const x = (clientX - (rect.left + offsetX)) / Math.max(1, renderWidth);
    const y = (clientY - (rect.top + offsetY)) / Math.max(1, renderHeight);
    if (x < 0 || y < 0 || x > 1 || y > 1) {
      return null;
    }

    return { x, y };
  };

  const sendRemoteControlInput = async (command: Omit<RemoteControlInputCommand, "clientId" | "sessionId">) => {
    if (!teacherHubRef.current || !selectedStudentId || !selectedRemoteControlSession || !isRemoteControlApproved) {
      return;
    }

    const payload: RemoteControlInputCommand = {
      clientId: selectedStudentId,
      sessionId: selectedRemoteControlSession.sessionId,
      ...command,
    };

    try {
      await teacherHubRef.current.invoke("SendRemoteControlInput", payload);
    } catch (error) {
      appendEvent(`Remote input send failed: ${describeError(error)}`);
    }
  };

  const mapRemoteMouseButton = (button: number): "Left" | "Right" | "Middle" =>
    button === 2 ? "Right" : button === 1 ? "Middle" : "Left";

  const requestBrowserNotificationPermission = async () => {
    if (!("Notification" in window)) {
      appendEvent("Browser notifications are not available.");
      return;
    }

    try {
      const permission = await Notification.requestPermission();
      appendEvent(`Browser notification permission: ${permission}`);
    } catch (error) {
      appendEvent(`Notification permission request failed: ${describeError(error)}`);
    }
  };

  useEffect(() => {
    if (!isRemoteViewOpen) {
      return;
    }

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape") {
        return;
      }

      if (isRemoteControlApproved) {
        if (event.ctrlKey && event.shiftKey) {
          event.preventDefault();
          setIsRemoteViewOpen(false);
        }

        return;
      }

      setIsRemoteViewOpen(false);
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [isRemoteViewOpen, isRemoteControlApproved]);

  useEffect(() => {
    if (!isRemoteViewOpen || !isRemoteControlApproved) {
      remotePressedKeysRef.current.clear();
      return;
    }

    const shouldIgnoreKey = (event: KeyboardEvent) =>
      event.isComposing || event.key === "Process" || event.key === "Dead";

    const handleKeyDown = (event: KeyboardEvent) => {
      if (shouldIgnoreKey(event)) {
        return;
      }

      if (event.ctrlKey && event.shiftKey && event.key === "Escape") {
        event.preventDefault();
        return;
      }

      event.preventDefault();
      remoteViewportRef.current?.focus();
      remotePressedKeysRef.current.add(event.key);

      void sendRemoteControlInput({
        kind: "KeyDown",
        key: event.key,
        code: event.code,
        ctrl: event.ctrlKey,
        alt: event.altKey,
        shift: event.shiftKey,
      });
    };

    const handleKeyUp = (event: KeyboardEvent) => {
      if (shouldIgnoreKey(event)) {
        return;
      }

      event.preventDefault();
      remotePressedKeysRef.current.delete(event.key);

      void sendRemoteControlInput({
        kind: "KeyUp",
        key: event.key,
        code: event.code,
        ctrl: event.ctrlKey,
        alt: event.altKey,
        shift: event.shiftKey,
      });
    };

    window.addEventListener("keydown", handleKeyDown, true);
    window.addEventListener("keyup", handleKeyUp, true);
    return () => {
      window.removeEventListener("keydown", handleKeyDown, true);
      window.removeEventListener("keyup", handleKeyUp, true);
      remotePressedKeysRef.current.clear();
    };
  }, [isRemoteViewOpen, isRemoteControlApproved, selectedStudentId, selectedRemoteControlSession?.sessionId]);

  useEffect(() => {
    if (!isRemoteViewOpen || !isRemoteControlApproved) {
      return;
    }

    remoteViewportRef.current?.focus();
  }, [isRemoteViewOpen, isRemoteControlApproved, selectedStudentId, selectedRemoteControlSession?.sessionId]);

  return (
    <div className="box-border h-full min-h-0 min-w-0 overflow-hidden px-2 py-2 sm:px-3 sm:py-3 lg:px-5 lg:py-4">
      <div className="mx-auto flex h-full max-w-[1880px] min-h-0 min-w-0 flex-col gap-3 overflow-x-hidden overflow-y-auto">
        <header className="px-1 py-1">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="space-y-0.5">
              <h1 className="text-xl font-semibold tracking-tight lg:text-2xl">{t("appTitle")}</h1>
              <p className="text-xs text-muted-foreground">{t("headerHint")}</p>
            </div>
            <div className="flex flex-wrap items-center justify-end gap-2">
              <div className="flex max-w-full flex-wrap items-center gap-1 rounded-md border border-border bg-card/50 p-0.5">
                <Button
                  variant={activeView === "monitoring" ? "default" : "ghost"}
                  size="sm"
                  className="h-7 px-2 text-[11px]"
                  onClick={() => setActiveView("monitoring")}
                >
                  {t("monitoringTitle")}
                </Button>
                <Button
                  variant={activeView === "chats" ? "default" : "ghost"}
                  size="sm"
                  className="h-7 px-2 text-[11px]"
                  onClick={() => setActiveView("chats")}
                >
                  <span className="inline-flex items-center gap-1.5">
                    <span>{t("chatsTab")}</span>
                    {unreadChatTotal > 0 ? (
                      <span className="inline-flex min-w-4 items-center justify-center rounded-full border border-amber-500/35 bg-amber-500/15 px-1 text-[10px] leading-4 text-amber-300">
                        {unreadChatTotal > 99 ? "99+" : unreadChatTotal}
                      </span>
                    ) : null}
                  </span>
                </Button>
                <Button
                  variant={activeView === "detection" ? "default" : "ghost"}
                  size="sm"
                  className="h-7 px-2 text-[11px]"
                  onClick={() => setActiveView("detection")}
                >
                  {t("aiDetectionTab")}
                </Button>
                <Button
                  variant={activeView === "settings" ? "default" : "ghost"}
                  size="sm"
                  className="h-7 px-2 text-[11px]"
                  onClick={() => setActiveView("settings")}
                >
                  {t("settingsTab")}
                </Button>
              </div>
              <Badge variant={connectionState === "connected" ? "success" : "warning"} className="px-2 py-0.5 text-[10px]">
                {connectionState === "connected" ? t("connected") : t("disconnected")}
              </Badge>
              <div className="hidden items-center gap-1 rounded-md bg-card/40 p-0.5 sm:flex">
                {(["ru", "en", "kz"] as UiLanguage[]).map((code) => (
                  <Button
                    key={code}
                    variant={lang === code ? "default" : "ghost"}
                    size="sm"
                    className="h-6 px-1.5 text-[10px] uppercase"
                    onClick={() => setLang(code)}
                  >
                    {code}
                  </Button>
                ))}
              </div>
              <ThemeToggle theme={theme} onToggle={() => setTheme((current) => (current === "dark" ? "light" : "dark"))} />
            </div>
          </div>
        </header>

        {activeView === "monitoring" ? (
        <div className="mt-1 grid min-h-0 flex-1 gap-3 lg:grid-cols-[minmax(0,1fr)_290px] 2xl:grid-cols-[minmax(0,1fr)_308px]">
          <Card className="overflow-visible">
            <CardContent className="grid gap-4 p-4 lg:grid-cols-[minmax(0,1fr)_300px] xl:grid-cols-[minmax(0,1fr)_320px]">
              <div className="flex flex-col gap-3">
                <section className="flex flex-col rounded-xl border border-border bg-background/75 p-3">
                  <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                    <p className="text-sm font-medium">{t("focusedView")}</p>
                    {selectedStudent ? (
                      <div className="flex items-center gap-2">
                        <Badge variant={selectedStudent.isOnline ? "success" : "outline"}>{selectedStudent.hostName}</Badge>
                        <span className="text-xs text-muted-foreground">{selectedStudent.localIpAddress ?? t("statusNoIp")}</span>
                      </div>
                    ) : null}
                  </div>
                  <p className="mb-2 text-xs text-muted-foreground">{t("focusedHint")}</p>
                  <div className="min-h-[360px] flex-1 overflow-hidden rounded-lg border border-border bg-muted/35 xl:min-h-[420px]">
                    {selectedFrame ? (
                      <img src={selectedFrame.url} alt={t("streamImageAlt")} className="h-full w-full object-contain" />
                    ) : (
                      <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
                        {t("selectStudentStream")}
                      </div>
                    )}
                  </div>
                </section>

                <section className="rounded-xl border border-border bg-background/75 p-3">
                  <div className="relative overflow-hidden rounded-lg border border-border/80 bg-card/55 p-3">
                    <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(circle_at_top_right,rgba(93,141,255,0.12),transparent_58%)]" />
                    <div className="relative">
                      <div className="flex flex-wrap items-start justify-between gap-2">
                        <div>
                          <p className="text-xs uppercase tracking-[0.12em] text-muted-foreground">{t("controlPanel")}</p>
                          <p className="mt-1 text-xs text-muted-foreground">{t("controlHint")}</p>
                        </div>
                        {selectedStudent ? (
                          <Badge variant={selectedStudent.isOnline ? "success" : "outline"}>{selectedStudent.hostName}</Badge>
                        ) : (
                          <Badge variant="outline">{t("selectStudentStream")}</Badge>
                        )}
                      </div>

                      <div className="mt-3 grid gap-2 sm:grid-cols-2">
                        <Button
                          variant="outline"
                          size="sm"
                          disabled={!canOpenRemoteView}
                          onClick={() => setIsRemoteViewOpen(true)}
                          className={cn(
                            "h-auto min-h-[58px] justify-start gap-3 rounded-lg px-3 py-2 text-left",
                            "border-primary/25 bg-primary/5 hover:border-primary/40 hover:bg-primary/10",
                            "disabled:border-border/80 disabled:bg-background/50",
                          )}
                        >
                          <span className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-md border border-primary/20 bg-primary/10 text-primary">
                            <IconMonitor className="h-4 w-4" />
                          </span>
                          <span className="flex min-w-0 flex-col items-start leading-tight">
                            <span className="truncate text-sm font-semibold">{t("manageComputer")}</span>
                            <span className="truncate text-[11px] font-normal text-muted-foreground">
                              {t("manageComputerHint")}
                            </span>
                          </span>
                        </Button>

                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => setIsSendModalOpen(true)}
                          className="h-auto min-h-[58px] justify-start gap-3 rounded-lg border-border/80 bg-background/60 px-3 py-2 text-left hover:bg-accent/35"
                        >
                          <span className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-md border border-border/80 bg-muted/35 text-foreground">
                            <IconUpload className="h-4 w-4" />
                          </span>
                          <span className="flex min-w-0 flex-col items-start leading-tight">
                            <span className="truncate text-sm font-semibold">{t("sendFilesToStudents")}</span>
                            <span className="truncate text-[11px] font-normal text-muted-foreground">
                              {interpolate(t("selectedTargetsCount"), { count: String(selectedTargetCount) })}
                            </span>
                          </span>
                        </Button>
                      </div>

                      <div className="mt-2 flex flex-wrap items-center gap-2">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setIsThumbnailsExpanded(true)}
                          className="h-8 rounded-full border border-border/80 bg-background/50 px-3 text-xs"
                        >
                          <IconGrid className="mr-1.5 h-3.5 w-3.5" />
                          {t("expandThumbnails")}
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setIsLogsOpen(true)}
                          className="h-8 rounded-full border border-border/80 bg-background/50 px-3 text-xs"
                        >
                          <IconList className="mr-1.5 h-3.5 w-3.5" />
                          {t("alertsAudit")}
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setIsTeacherChatModalOpen(true)}
                          className="h-8 rounded-full border border-border/80 bg-background/50 px-3 text-xs"
                        >
                          <IconChat className="mr-1.5 h-3.5 w-3.5" />
                          Chat
                          {selectedStudentId && (chatUnreadByStudent[selectedStudentId] ?? 0) > 0 ? (
                            <span className="ml-1 inline-flex min-w-4 items-center justify-center rounded-full border border-sky-500/40 bg-sky-500/10 px-1 text-[10px] leading-4 text-sky-300">
                              {chatUnreadByStudent[selectedStudentId]! > 99 ? "99+" : chatUnreadByStudent[selectedStudentId]}
                            </span>
                          ) : null}
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setIsTeacherTtsModalOpen(true)}
                          className="h-8 rounded-full border border-border/80 bg-background/50 px-3 text-xs"
                        >
                          <IconVolume className="mr-1.5 h-3.5 w-3.5" />
                          TTS
                        </Button>
                      </div>

                      <div className="mt-2 rounded-lg border border-border/80 bg-background/55 p-2.5">
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <div>
                            <p className="text-[11px] font-semibold uppercase tracking-[0.08em] text-muted-foreground">
                              Live captions (STT)
                            </p>
                            <p className="mt-0.5 text-[11px] text-muted-foreground">
                              {isLiveSttActive
                                ? `Streaming to ${activeLiveSttTarget?.hostName ?? liveSttTargetClientId ?? "student"}`
                                : "Microphone off"}
                              {" • "}
                              {sttSelectedLanguageLabel}
                            </p>
                          </div>
                          <Button
                            type="button"
                            variant={isLiveSttActive ? "destructive" : "outline"}
                            size="sm"
                            className="h-9 min-w-9 rounded-full px-0"
                            aria-label={isLiveSttActive ? "Disable live captions microphone" : "Enable live captions microphone"}
                            title={isLiveSttActive ? "Turn off live captions microphone" : "Turn on live captions microphone"}
                            disabled={!selectedStudent || !selectedStudent.isOnline}
                            onClick={() => {
                              if (isLiveSttActive) {
                                void stopLiveStt("Live captions stopped.", { tone: "neutral" });
                                return;
                              }
                              void startLiveStt();
                            }}
                          >
                            {isLiveSttActive ? <IconMicOff className="h-4 w-4" /> : <IconMic className="h-4 w-4" />}
                          </Button>
                        </div>

                        {teacherSttStatus ? (
                          <div
                            className={cn(
                              "mt-2 rounded-md border px-2 py-1.5 text-xs",
                              teacherSttStatus.tone === "success" && "border-emerald-500/35 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300",
                              teacherSttStatus.tone === "error" && "border-destructive/40 bg-destructive/10 text-destructive",
                              teacherSttStatus.tone === "neutral" && "border-border/80 bg-muted/20 text-muted-foreground",
                            )}
                          >
                            {teacherSttStatus.text}
                          </div>
                        ) : null}

                        {liveSttPreviewText ? (
                          <p className="mt-2 line-clamp-2 text-xs text-foreground/90">{liveSttPreviewText}</p>
                        ) : null}
                      </div>
                    </div>
                  </div>
                  <p className="mt-2 text-xs text-muted-foreground">{uploadStatus || t("noActiveTransfers")}</p>
                </section>

              </div>

              <aside className="sticky top-3 flex min-h-0 self-start flex-col rounded-xl border border-border bg-background/75 p-3 max-h-[calc(100vh-7.5rem)]">
                <div className="mb-2 space-y-2">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="flex items-center gap-2">
                      <p className="text-sm font-medium">{t("students")}</p>
                      <Badge variant="secondary">{sortedStudents.length}</Badge>
                    </div>
                    <Button size="sm" variant="outline" onClick={() => setIsThumbnailsExpanded(true)}>
                      {t("expandThumbnails")}
                    </Button>
                  </div>
                  <p className="text-xs text-muted-foreground">{t("studentsHint")}</p>
                  <Input value={search} onChange={(event) => setSearch(event.target.value)} placeholder={t("searchPlaceholder")} />
                  <div className="flex gap-2">
                    <Button size="sm" variant={onlyOnline ? "default" : "outline"} onClick={() => setOnlyOnline(true)}>
                      {t("online")}
                    </Button>
                    <Button size="sm" variant={!onlyOnline ? "default" : "outline"} onClick={() => setOnlyOnline(false)}>
                      {t("all")}
                    </Button>
                  </div>
                </div>

                <ScrollArea.Root className="mt-2 min-h-0 flex-1 overflow-hidden max-h-[calc(100vh-18rem)]">
                  <ScrollArea.Viewport className="h-full pr-2">
                    <div className="grid grid-cols-1 gap-2">
                      {sortedStudents.map((student) => {
                        const frame = frames[student.clientId];
                        const isFocused = selectedStudentId === student.clientId;
                        const isTarget = selectedTargets.has(student.clientId);
                        const unreadCount = chatUnreadByStudent[student.clientId] ?? 0;

                        return (
                          <button
                            key={student.clientId}
                            type="button"
                            onClick={() => setSelectedStudentId(student.clientId)}
                            className={cn(
                              "rounded-md border p-2 text-left transition hover:bg-accent/60",
                              isFocused ? "border-primary bg-primary/10" : "border-border",
                              unreadCount > 0 && !isFocused && "border-sky-500/40 bg-sky-500/5",
                            )}
                          >
                            <div className="mb-1.5 h-20 overflow-hidden rounded border border-border bg-muted/40">
                              {frame ? (
                                <img src={frame.url} alt={student.hostName} className="h-full w-full object-cover" />
                              ) : (
                                <div className="flex h-full items-center justify-center text-xs text-muted-foreground">{t("noFrame")}</div>
                              )}
                            </div>
                            <div className="mb-1 flex items-center justify-between gap-1">
                              <div className="flex min-w-0 items-center gap-1">
                                <p className="truncate text-xs font-medium">{student.hostName}</p>
                                {unreadCount > 0 ? (
                                  <Badge variant="outline" className="h-4 border-sky-500/40 bg-sky-500/10 px-1 text-[9px] text-sky-300">
                                    {t("chatsTab")}: {unreadCount}
                                  </Badge>
                                ) : null}
                                {(handRaisedUntil[student.clientId] ?? 0) > uiClock ? (
                                  <Badge variant="warning" className="h-4 px-1 text-[9px]">
                                    {t("handRaised")}
                                  </Badge>
                                ) : null}
                              </div>
                              <button
                                type="button"
                                className="inline-flex h-6 w-6 items-center justify-center rounded border border-destructive/40 text-xs text-destructive transition hover:bg-destructive/10"
                                title={t("remove")}
                                onClick={(event) => {
                                  event.stopPropagation();
                                  if (!confirm(interpolate(t("removeConfirm"), { name: student.hostName }))) {
                                    return;
                                  }

                                  removeStudent.mutate(student.clientId);
                                }}
                                disabled={removeStudent.isPending}
                              >
                                ×
                              </button>
                            </div>
                            <p className="truncate text-[11px] text-muted-foreground">{student.userName}</p>
                            <p className="truncate text-[11px] text-muted-foreground">
                              {frame ? new Date(frame.capturedAtUtc).toLocaleTimeString() : t("noUpdates")}
                            </p>
                            <p className="truncate text-[11px] text-muted-foreground">
                              {t("studentAlertsCount")}: {student.alertCount ?? 0}
                            </p>
                            {student.lastDetectionAtUtc ? (
                              <p className="truncate text-[11px] text-muted-foreground">
                                {t("studentLastDetection")}: {new Date(student.lastDetectionAtUtc).toLocaleTimeString()}{" "}
                                {student.lastDetectionClass ? `(${localizeDetectionClass(student.lastDetectionClass)})` : ""}
                              </p>
                            ) : null}
                            <div className="mt-1.5 flex items-center justify-between gap-1">
                              <Badge variant={student.isOnline ? "success" : "outline"} className="text-[10px]">
                                {student.isOnline ? t("online") : t("offline")}
                              </Badge>
                              <label className="inline-flex items-center gap-1 rounded border border-border px-1.5 py-0.5 text-[11px] text-muted-foreground">
                                <input
                                  type="checkbox"
                                  checked={isTarget}
                                  onChange={(event) => {
                                    event.stopPropagation();
                                    toggleTarget(student.clientId);
                                  }}
                                />
                                {t("target")}
                              </label>
                            </div>
                          </button>
                        );
                      })}
                    </div>
                    {sortedStudents.length === 0 ? <p className="mt-2 text-sm text-muted-foreground">{t("noMatches")}</p> : null}
                  </ScrollArea.Viewport>
                  <ScrollArea.Scrollbar className="w-2" orientation="vertical">
                    <ScrollArea.Thumb className="rounded bg-muted-foreground/30" />
                  </ScrollArea.Scrollbar>
                </ScrollArea.Root>
              </aside>
            </CardContent>
          </Card>

          <Card className="flex min-h-0 flex-col overflow-visible">
            <CardHeader className="pb-2">
              <CardTitle>{t("controlPanel")}</CardTitle>
              <CardDescription>{t("logsHint")}</CardDescription>
            </CardHeader>
            <CardContent className="flex min-h-0 flex-1 flex-col gap-3 pb-3">
              <div className="grid grid-cols-1 gap-2">
                <div className="flex items-center justify-between rounded-md border border-border/80 bg-muted/25 px-2.5 py-1.5">
                  <p className="text-[11px] text-muted-foreground">{t("knownDevices")}</p>
                  <p className="text-base font-semibold leading-none">{Object.keys(students).length}</p>
                </div>
                <div className="flex items-center justify-between rounded-md border border-border/80 bg-muted/25 px-2.5 py-1.5">
                  <p className="text-[11px] text-muted-foreground">{t("studentsOnline")}</p>
                  <p className="text-base font-semibold leading-none">{onlineStudents.length}</p>
                </div>
                <div className="flex items-center justify-between rounded-md border border-border/80 bg-muted/25 px-2.5 py-1.5">
                  <p className="text-[11px] text-muted-foreground">{t("selectedTargets")}</p>
                  <p className="text-base font-semibold leading-none">{selectedTargetCount}</p>
                </div>
              </div>

              <div className="rounded-lg border border-border bg-background/70 p-3">
                <p className="mb-2 text-xs uppercase tracking-[0.11em] text-muted-foreground">{t("pairingPin")}</p>
                <p className="mb-2 text-xs text-muted-foreground">{t("pairingHint")}</p>
                <div className="space-y-2">
                  <Button className="w-full" size="sm" onClick={() => generatePin.mutate()} disabled={generatePin.isPending}>
                    {t("generatePin")}
                  </Button>
                  <div className="rounded-md border border-border bg-muted/40 px-2 py-2 text-center text-xs">
                    <p className="font-semibold tracking-[0.18em]">{pin?.pinCode ?? "------"}</p>
                    <p className="mt-1 text-muted-foreground">
                      {pin ? `${t("expiresWord")} ${new Date(pin.expiresAtUtc).toLocaleTimeString()}` : " "}
                    </p>
                  </div>
                </div>
              </div>

              <AccessibilityAssignmentCard
                lang={lang}
                selectedStudent={selectedStudent}
                isPending={assignAccessibilityProfile.isPending}
                statusText={accessibilityAssignStatus?.text}
                statusTone={accessibilityAssignStatus?.tone}
                onAssign={(profile) => {
                  if (!selectedStudentId) {
                    setAccessibilityAssignStatus({ tone: "error", text: "Select a device first." });
                    return;
                  }

                  setAccessibilityAssignStatus({ tone: "neutral", text: `Dispatching ${profile.activePreset} profile...` });
                  assignAccessibilityProfile.mutate({ clientId: selectedStudentId, profile });
                }}
              />

              <div className="mt-auto space-y-2">
                <div className="rounded-md border border-border/80 bg-muted/20 px-2.5 py-2">
                  <div className="mb-1 flex items-center justify-between gap-2">
                    <p className="text-[11px] font-medium uppercase tracking-[0.08em] text-muted-foreground">{t("lastEventPrefix")}</p>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => setIsLogsOpen(true)}
                      className="h-6 px-2 text-[10px]"
                      aria-label={t("alertsAudit")}
                    >
                      <IconList className="h-3.5 w-3.5" />
                    </Button>
                  </div>
                  <p className="line-clamp-3 text-xs text-muted-foreground">
                    {events.length === 0 ? t("noEvents") : humanizeProgramNames(events[0])}
                  </p>
                </div>
              </div>
            </CardContent>
          </Card>
        </div>
        ) : activeView === "chats" ? (
          <Card className="mt-1 min-h-0 flex-1 overflow-hidden">
            <CardHeader className="pb-2">
              <CardTitle>{t("chatsTitle")}</CardTitle>
              <CardDescription>{t("chatsDesc")}</CardDescription>
            </CardHeader>
            <CardContent className="grid h-full min-h-0 gap-3 lg:grid-cols-[minmax(0,1fr)_320px]">
              <div className="min-h-0">
              </div>

              <aside className="sticky top-3 flex min-h-0 self-start flex-col rounded-xl border border-border bg-background/75 p-3 max-h-[calc(100vh-7.5rem)]">
                <div className="mb-2 flex items-center justify-between gap-2">
                  <div className="flex items-center gap-2">
                    <p className="text-sm font-medium">{t("students")}</p>
                    <Badge variant="secondary">{sortedStudents.length}</Badge>
                  </div>
                  <Badge variant={unreadChatTotal > 0 ? "warning" : "outline"}>{t("unreadMessages")}: {unreadChatTotal}</Badge>
                </div>
                <p className="mb-2 text-xs text-muted-foreground">{t("studentsHint")}</p>
                <Input value={search} onChange={(event) => setSearch(event.target.value)} placeholder={t("searchPlaceholder")} />
                <div className="mt-2 flex gap-2">
                  <Button size="sm" variant={onlyOnline ? "default" : "outline"} onClick={() => setOnlyOnline(true)}>
                    {t("online")}
                  </Button>
                  <Button size="sm" variant={!onlyOnline ? "default" : "outline"} onClick={() => setOnlyOnline(false)}>
                    {t("all")}
                  </Button>
                </div>

                <ScrollArea.Root className="mt-3 min-h-0 flex-1 overflow-hidden max-h-[calc(100vh-18rem)]">
                  <ScrollArea.Viewport className="h-full pr-2">
                    <div className="space-y-2">
                      {sortedStudents.map((student) => {
                        const isSelected = selectedStudentId === student.clientId;
                        const unreadCount = chatUnreadByStudent[student.clientId] ?? 0;
                        const studentMessages = chatByStudent[student.clientId] ?? [];
                        const lastMessage = studentMessages.length > 0 ? studentMessages[studentMessages.length - 1] : null;

                        return (
                          <button
                            key={student.clientId}
                            type="button"
                            onClick={() => {
                              setSelectedStudentId(student.clientId);
                              markChatRead(student.clientId);
                            }}
                            className={cn(
                              "w-full rounded-md border p-2 text-left transition hover:bg-accent/60",
                              isSelected ? "border-primary bg-primary/10" : "border-border",
                            )}
                          >
                            <div className="flex items-center justify-between gap-2">
                              <div className="min-w-0">
                                <p className="truncate text-sm font-medium">{student.hostName}</p>
                                <p className="truncate text-[11px] text-muted-foreground">{student.userName}</p>
                              </div>
                              <div className="flex shrink-0 items-center gap-1">
                                {unreadCount > 0 ? (
                                  <Badge variant="warning" className="px-1.5 text-[10px]">
                                    {unreadCount}
                                  </Badge>
                                ) : null}
                                <Badge variant={student.isOnline ? "success" : "outline"} className="text-[10px]">
                                  {student.isOnline ? t("online") : t("offline")}
                                </Badge>
                              </div>
                            </div>
                            <p className="mt-1 truncate text-[11px] text-muted-foreground">
                              {lastMessage?.text?.trim()
                                ? lastMessage.text.trim().replace(/\s+/g, " ").slice(0, 80)
                                : t("noEvents")}
                            </p>
                          </button>
                        );
                      })}
                      {sortedStudents.length === 0 ? <p className="text-sm text-muted-foreground">{t("noMatches")}</p> : null}
                    </div>
                  </ScrollArea.Viewport>
                  <ScrollArea.Scrollbar className="w-2" orientation="vertical">
                    <ScrollArea.Thumb className="rounded bg-muted-foreground/30" />
                  </ScrollArea.Scrollbar>
                </ScrollArea.Root>
              </aside>
            </CardContent>
          </Card>
        ) : activeView === "detection" ? (
          <Card className="mt-1 min-h-0 flex-1 overflow-hidden">
            <CardHeader className="pb-2">
              <CardTitle>{t("detectionTitle")}</CardTitle>
              <CardDescription>{t("detectionDesc")}</CardDescription>
            </CardHeader>
            <CardContent className="grid min-h-0 h-full gap-3 lg:grid-cols-[340px_minmax(0,1fr)]">
              <div className="min-h-0 space-y-3 overflow-y-auto pr-1">
                <div className="rounded-lg border border-border bg-background/70 p-3 space-y-2">
                  <p className="text-xs uppercase tracking-[0.11em] text-muted-foreground">{t("detectionFilters")}</p>
                  <select
                    className="h-9 w-full rounded-md border border-input bg-background px-2 text-sm"
                    value={aiFilterStudent}
                    onChange={(event) => setAiFilterStudent(event.target.value)}
                  >
                    <option value="all">{t("detectionAllDevices")}</option>
                    {Object.values(students).map((student) => (
                      <option key={student.clientId} value={student.clientId}>{student.hostName}</option>
                    ))}
                  </select>
                  <select
                    className="h-9 w-full rounded-md border border-input bg-background px-2 text-sm"
                    value={aiFilterClass}
                    onChange={(event) => setAiFilterClass(event.target.value)}
                  >
                    <option value="all">{t("detectionAllClasses")}</option>
                    {["ChatGpt", "Claude", "Gemini", "Copilot", "Perplexity", "DeepSeek", "Poe", "Grok", "Qwen", "Mistral", "MetaAi", "UnknownAi"].map((value) => (
                      <option key={value} value={value}>{localizeDetectionClass(value)}</option>
                    ))}
                  </select>
                  <select
                    className="h-9 w-full rounded-md border border-input bg-background px-2 text-sm"
                    value={aiFilterTime}
                    onChange={(event) => setAiFilterTime(event.target.value as "all" | "15m" | "1h" | "24h")}
                  >
                    <option value="all">{t("detectionAnyTime")}</option>
                    <option value="15m">{t("detectionLast15m")}</option>
                    <option value="1h">{t("detectionLast1h")}</option>
                    <option value="24h">{t("detectionLast24h")}</option>
                  </select>
                </div>

                <div className="rounded-lg border border-border bg-background/70 p-3 space-y-2">
                  <p className="text-xs uppercase tracking-[0.11em] text-muted-foreground">{t("detectionPolicy")}</p>
                  <p className="text-xs text-muted-foreground">{t("detectionProductionLocked")}</p>
                  <div className="grid grid-cols-2 gap-2 text-xs">
                    <div className="rounded-md border border-border/80 bg-muted/25 px-2 py-1.5">
                      <p className="text-muted-foreground">{t("detectionMetadataThreshold")}</p>
                      <p className="font-semibold">{(detectionPolicy?.metadataThreshold ?? 0.64).toFixed(2)}</p>
                    </div>
                    <div className="rounded-md border border-border/80 bg-muted/25 px-2 py-1.5">
                      <p className="text-muted-foreground">{t("detectionMlThreshold")}</p>
                      <p className="font-semibold">{(detectionPolicy?.mlThreshold ?? 0.72).toFixed(2)}</p>
                    </div>
                    <div className="rounded-md border border-border/80 bg-muted/25 px-2 py-1.5">
                      <p className="text-muted-foreground">{t("detectionCooldownSeconds")}</p>
                      <p className="font-semibold">{detectionPolicy?.cooldownSeconds ?? 10}s</p>
                    </div>
                    <div className="rounded-md border border-border/80 bg-muted/25 px-2 py-1.5">
                      <p className="text-muted-foreground">{t("detectionEnable")}</p>
                      <p className="font-semibold">{(detectionPolicy?.enabled ?? true) ? t("enabled") : t("disabled")}</p>
                    </div>
                  </div>
                </div>
              </div>

              <div className="min-h-0 rounded-lg border border-border bg-background/70 p-3">
                <div className="mb-2 flex items-center justify-between">
                  <p className="text-sm font-medium">{t("detectionLiveAlerts")}</p>
                  <Badge variant="secondary">{filteredAiAlerts.length}</Badge>
                </div>
                <ScrollArea.Root className="h-full overflow-hidden">
                  <ScrollArea.Viewport className="h-full pr-2">
                    <div className="space-y-2">
                      {filteredAiAlerts.length === 0 ? (
                        <p className="text-sm text-muted-foreground">{t("detectionNoEvents")}</p>
                      ) : null}
                      {filteredAiAlerts.map((alert) => (
                        <div key={alert.eventId} className="rounded-md border border-border bg-card/70 p-2 text-xs">
                          <div className="flex items-center justify-between gap-2">
                            <span className="font-semibold">{students[alert.studentId]?.hostName ?? alert.studentDisplayName}</span>
                            <Badge variant="warning">{localizeDetectionClass(alert.detectionClass)}</Badge>
                          </div>
                          <p className="mt-1 text-muted-foreground">
                            {new Date(alert.timestampUtc).toLocaleString()} | {t("detectionConfidence")}: {alert.confidence.toFixed(2)} | {t("detectionSource")}: {localizeStageSource(alert.stageSource)}
                          </p>
                          <p className="mt-1">{humanizeProgramNames(alert.reason)}</p>
                        </div>
                      ))}
                    </div>
                  </ScrollArea.Viewport>
                  <ScrollArea.Scrollbar className="w-2" orientation="vertical">
                    <ScrollArea.Thumb className="rounded bg-muted-foreground/30" />
                  </ScrollArea.Scrollbar>
                </ScrollArea.Root>
              </div>
            </CardContent>
          </Card>
        ) : (
          <Card className="mt-1 min-h-0 flex-1 overflow-hidden">
            <CardHeader className="pb-2">
              <CardTitle>{t("settingsTitle")}</CardTitle>
              <CardDescription>{t("settingsDesc")}</CardDescription>
            </CardHeader>
            <CardContent className="min-h-0 h-full">
              <div className="rounded-lg border border-border bg-background/70 p-3 space-y-3">
                <label className="flex items-start gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={aiWarningsEnabled}
                    onChange={(event) => setAiWarningsEnabled(event.target.checked)}
                  />
                  <span>
                    <span className="font-medium">{t("settingsAiWarningsEnabled")}</span>
                    <span className="mt-0.5 block text-xs text-muted-foreground">{t("settingsAiWarningsHint")}</span>
                  </span>
                </label>

                <label className="flex items-start gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={desktopNotificationsEnabled}
                    onChange={(event) => setDesktopNotificationsEnabled(event.target.checked)}
                  />
                  <span>
                    <span className="font-medium">{t("settingsDesktopNotifications")}</span>
                    <span className="mt-0.5 block text-xs text-muted-foreground">{t("settingsDesktopNotificationsHint")}</span>
                  </span>
                </label>

                {desktopNotificationsEnabled && "Notification" in window && Notification.permission !== "granted" ? (
                  <Button size="sm" variant="secondary" onClick={requestBrowserNotificationPermission}>
                    {t("settingsRequestPermission")}
                  </Button>
                ) : null}

                <label className="flex items-start gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={soundNotificationsEnabled}
                    onChange={(event) => setSoundNotificationsEnabled(event.target.checked)}
                  />
                  <span>
                    <span className="font-medium">{t("settingsSoundEnabled")}</span>
                    <span className="mt-0.5 block text-xs text-muted-foreground">{t("settingsSoundEnabledHint")}</span>
                  </span>
                </label>

                <div className="space-y-1">
                  <div className="flex items-center justify-between text-sm">
                    <span className="font-medium">{t("settingsVolume")}</span>
                    <span className="rounded border border-border/70 bg-muted/25 px-1.5 py-0.5 text-xs font-semibold">{notificationVolume}%</span>
                  </div>
                  <input
                    className="slider-fancy w-full"
                    type="range"
                    min={0}
                    max={100}
                    step={1}
                    value={notificationVolume}
                    onChange={(event) => setNotificationVolume(Math.min(100, Math.max(0, Number(event.target.value))))}
                  />
                </div>

                <div className="rounded-lg border border-border/80 bg-muted/20 p-3 space-y-2">
                  <div>
                    <p className="text-sm font-medium">{t("settingsTtsProxyTitle")}</p>
                    <p className="mt-0.5 text-xs text-muted-foreground">{t("settingsTtsProxyDesc")}</p>
                  </div>

                  <label className="block space-y-1">
                    <span className="text-xs font-medium text-muted-foreground">{t("settingsTtsProxyUrl")}</span>
                    <Input
                      value={selfHostTtsUrl}
                      onChange={(event) => setSelfHostTtsUrl(event.target.value)}
                      placeholder="https://tts.kilocraft.org"
                      autoComplete="off"
                      spellCheck={false}
                    />
                  </label>

                  <label className="block space-y-1">
                    <div className="flex items-center justify-between gap-2">
                      <span className="text-xs font-medium text-muted-foreground">{t("settingsTtsProxyToken")}</span>
                      <span className="text-[10px] text-muted-foreground">{selfHostTtsToken.trim() ? "Configured" : t("disabled")}</span>
                    </div>
                    <Input
                      type="password"
                      value={selfHostTtsToken}
                      onChange={(event) => setSelfHostTtsToken(event.target.value)}
                      placeholder="Bearer token"
                      autoComplete="off"
                      spellCheck={false}
                      className="font-mono text-xs"
                    />
                    <p className="text-[11px] text-muted-foreground">{t("settingsTtsProxyTokenHint")}</p>
                  </label>
                </div>

                <div className="rounded-lg border border-border/80 bg-muted/20 p-3 space-y-2">
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div>
                      <p className="text-sm font-medium">Live captions (STT)</p>
                      <p className="mt-0.5 text-xs text-muted-foreground">
                        Microphone and speech language for teacher live subtitles in monitoring. Uses the same speech proxy URL/token above.
                      </p>
                    </div>
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      onClick={() => void refreshSttMicrophones({ requestPermission: true })}
                      disabled={sttAudioInputsLoading}
                    >
                      {sttAudioInputsLoading ? "Refreshing..." : "Refresh microphones"}
                    </Button>
                  </div>

                  <div className="grid gap-2 sm:grid-cols-2">
                    <label className="block space-y-1">
                      <span className="text-xs font-medium text-muted-foreground">Microphone</span>
                      <select
                        className="h-10 w-full rounded-md border border-input bg-background px-3 text-sm"
                        value={sttMicrophoneDeviceId}
                        onChange={(event) => setSttMicrophoneDeviceId(event.target.value)}
                      >
                        {sttAudioInputs.length === 0 ? <option value="">No microphones detected</option> : null}
                        {sttAudioInputs.map((device) => (
                          <option key={device.deviceId} value={device.deviceId}>
                            {device.label}
                          </option>
                        ))}
                      </select>
                      <p className="text-[11px] text-muted-foreground">
                        {sttAudioInputs.length > 0
                          ? `${sttAudioInputs.length} input(s) found`
                          : "If labels are empty or no devices show up, click refresh and allow microphone access."}
                      </p>
                    </label>

                    <label className="block space-y-1">
                      <span className="text-xs font-medium text-muted-foreground">Recognition language</span>
                      <select
                        className="h-10 w-full rounded-md border border-input bg-background px-3 text-sm"
                        value={sttLanguageCode}
                        onChange={(event) => setSttLanguageCode(event.target.value)}
                      >
                        <option value="auto">Auto detect</option>
                        <option value="ru">Russian (ru)</option>
                        <option value="en">English (en)</option>
                        <option value="kk">Kazakh (kk)</option>
                      </select>
                      <p className="text-[11px] text-muted-foreground">
                        Choose the spoken language for faster and more stable transcription.
                      </p>
                    </label>
                  </div>
                </div>
              </div>
            </CardContent>
          </Card>
        )}
      </div>

      {toasts.length > 0 ? (
        <div className="pointer-events-none fixed left-1/2 top-4 z-[70] flex w-[min(92vw,560px)] -translate-x-1/2 flex-col gap-2">
          {toasts.map((toast) => {
            const isHandRaise = toast.kind === "handRaise";
            const isChatMessage = toast.kind === "chatMessage";
            const title = isHandRaise ? t("toastSignalTitle") : isChatMessage ? t("toastChatTitle") : t("toastAlertTitle");
            const message = isHandRaise
              ? interpolate(t("toastHandRaise"), { name: toast.studentName })
              : isChatMessage
                ? interpolate(t("toastChatReceived"), { name: toast.studentName, text: toast.messageText ?? "..." })
                : interpolate(t("toastAiDetected"), {
                    name: toast.studentName,
                    className: localizeDetectionClass(toast.detectionClass ?? "UnknownAi"),
                    confidence: (toast.confidence ?? 0).toFixed(2),
                  });

            return (
              <div
                key={toast.id}
                className={cn(
                  "pointer-events-auto rounded-lg border px-3 py-2 shadow-lg backdrop-blur",
                  isHandRaise
                    ? "border-amber-500/50 bg-amber-500/12 text-foreground"
                    : isChatMessage
                      ? "border-sky-500/45 bg-sky-500/12 text-foreground"
                      : "border-rose-500/45 bg-rose-500/12 text-foreground",
                )}
              >
                <p className="text-[11px] font-semibold uppercase tracking-[0.12em] opacity-90">{title}</p>
                <p className="mt-0.5 text-sm">{message}</p>
              </div>
            );
          })}
        </div>
      ) : null}

      {isRemoteViewOpen ? (
        <div className="fixed inset-0 z-[60] flex flex-col bg-background/95 backdrop-blur-sm">
          <div className="border-b border-border/80 bg-card/70 px-3 py-2 sm:px-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <p className="text-sm font-semibold">{t("remoteViewTitle")}</p>
                  {selectedStudent ? (
                    <Badge variant={selectedStudent.isOnline ? "success" : "outline"}>{selectedStudent.hostName}</Badge>
                  ) : null}
                </div>
                <p className="mt-0.5 truncate text-xs text-muted-foreground">{t("remoteViewDesc")}</p>
              </div>
              <Button variant="outline" size="sm" className="gap-1.5" onClick={() => setIsRemoteViewOpen(false)}>
                <IconClose className="h-3.5 w-3.5" />
                {t("close")}
              </Button>
            </div>
          </div>

          <div className="min-h-0 flex-1 p-3 sm:p-4">
            <div className="flex h-full min-h-0 flex-col gap-3">
              <div className="rounded-lg border border-border/80 bg-card/55 px-3 py-2 text-xs text-muted-foreground">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="inline-flex items-center rounded-full border border-primary/20 bg-primary/10 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-[0.1em] text-primary">
                      {t("manageComputer")}
                    </span>
                    <span>{t("remoteViewPrepHint")}</span>
                    {selectedRemoteControlSession ? (
                      <span
                        className={cn(
                          "inline-flex items-center rounded-full border px-2 py-0.5 text-[10px] font-semibold uppercase tracking-[0.08em]",
                          selectedRemoteControlSession.state === "Approved"
                            ? "border-emerald-500/35 bg-emerald-500/10 text-emerald-400"
                            : selectedRemoteControlSession.state === "PendingApproval"
                              ? "border-amber-500/35 bg-amber-500/10 text-amber-400"
                              : selectedRemoteControlSession.state === "Rejected" || selectedRemoteControlSession.state === "Error"
                                ? "border-rose-500/35 bg-rose-500/10 text-rose-400"
                                : "border-border/80 bg-background/50 text-muted-foreground",
                        )}
                      >
                        {selectedRemoteControlSession.state}
                      </span>
                    ) : null}
                  </div>
                  <div className="flex flex-wrap items-center gap-2">
                    <Button
                      size="sm"
                      variant={isRemoteControlApproved ? "secondary" : "default"}
                      disabled={!selectedStudent || !selectedStudent.isOnline || isRemoteControlPending}
                      onClick={() => void requestRemoteControlSession()}
                      className="gap-1.5"
                    >
                      <IconMonitor className="h-3.5 w-3.5" />
                      {isRemoteControlPending ? t("remoteControlWaiting") : t("remoteControlRequest")}
                    </Button>
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={!selectedRemoteControlSession || (selectedRemoteControlSession.state !== "Approved" && selectedRemoteControlSession.state !== "PendingApproval")}
                      onClick={() => void stopRemoteControlSession()}
                    >
                      {t("remoteControlStop")}
                    </Button>
                  </div>
                </div>
                {selectedRemoteControlSession?.message ? (
                  <p className="mt-1 text-[11px] text-muted-foreground">{selectedRemoteControlSession.message}</p>
                ) : null}
              </div>

              <div className="min-h-0 flex-1 overflow-hidden rounded-xl border border-border bg-muted/25">
                {selectedFrame ? (
                  <div
                    ref={remoteViewportRef}
                    tabIndex={0}
                    className={cn(
                      "relative h-full w-full bg-black/80 outline-none",
                      isRemoteControlApproved ? "cursor-crosshair" : "cursor-default",
                    )}
                    onContextMenu={(event) => event.preventDefault()}
                    onPointerMove={(event) => {
                      if (!isRemoteControlApproved) {
                        return;
                      }

                      const now = performance.now();
                      if (now - remoteLastPointerSendAtRef.current < 24) {
                        return;
                      }

                      const point = getRemotePoint(event.clientX, event.clientY);
                      if (!point) {
                        return;
                      }

                      remoteLastPointerSendAtRef.current = now;
                      void sendRemoteControlInput({
                        kind: "MouseMove",
                        x: point.x,
                        y: point.y,
                      });
                    }}
                    onPointerDown={(event) => {
                      if (!isRemoteControlApproved) {
                        return;
                      }

                      try {
                        event.currentTarget.setPointerCapture(event.pointerId);
                      } catch {
                        // Some browsers may reject pointer capture in edge cases.
                      }

                      remoteViewportRef.current?.focus();
                      const point = getRemotePoint(event.clientX, event.clientY);
                      if (!point) {
                        return;
                      }

                      event.preventDefault();
                      void sendRemoteControlInput({
                        kind: "MouseDown",
                        x: point.x,
                        y: point.y,
                        button: mapRemoteMouseButton(event.button),
                      });
                    }}
                    onPointerUp={(event) => {
                      if (!isRemoteControlApproved) {
                        return;
                      }

                      try {
                        event.currentTarget.releasePointerCapture(event.pointerId);
                      } catch {
                        // Ignore when capture was not active.
                      }

                      const point = getRemotePoint(event.clientX, event.clientY);
                      if (!point) {
                        return;
                      }

                      event.preventDefault();
                      void sendRemoteControlInput({
                        kind: "MouseUp",
                        x: point.x,
                        y: point.y,
                        button: mapRemoteMouseButton(event.button),
                      });
                    }}
                    onWheel={(event) => {
                      if (!isRemoteControlApproved) {
                        return;
                      }

                      const point = getRemotePoint(event.clientX, event.clientY);
                      if (!point) {
                        return;
                      }

                      event.preventDefault();
                      void sendRemoteControlInput({
                        kind: "MouseWheel",
                        x: point.x,
                        y: point.y,
                        wheelDelta: Math.round(-event.deltaY),
                      });
                    }}
                  >
                    <img src={selectedFrame.url} alt={t("streamImageAlt")} className="h-full w-full object-contain" />
                    {isRemoteControlApproved ? (
                      <div className="pointer-events-none absolute right-3 top-3 rounded-full border border-emerald-400/30 bg-emerald-400/12 px-2 py-1 text-[10px] font-semibold uppercase tracking-[0.08em] text-emerald-300">
                        {t("remoteControlActiveHint")}
                      </div>
                    ) : null}
                    <div className="pointer-events-none absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/60 to-transparent p-3">
                      <div className="flex flex-wrap items-center gap-2 text-xs text-white/90">
                        {selectedStudent ? <span>{selectedStudent.hostName}</span> : null}
                        <span className="opacity-70">•</span>
                        <span>{new Date(selectedFrame.capturedAtUtc).toLocaleTimeString()}</span>
                        {selectedStudent?.localIpAddress ? (
                          <>
                            <span className="opacity-70">•</span>
                            <span>{selectedStudent.localIpAddress}</span>
                          </>
                        ) : null}
                      </div>
                      <div className="mt-1 text-[11px] text-white/70">
                        {isRemoteControlApproved ? t("remoteControlInputHint") : t("remoteControlRequestHint")}
                      </div>
                    </div>
                  </div>
                ) : (
                  <div className="flex h-full items-center justify-center p-6">
                    <div className="max-w-md rounded-xl border border-border/80 bg-background/70 p-5 text-center">
                      <div className="mx-auto mb-3 inline-flex h-11 w-11 items-center justify-center rounded-full border border-border bg-muted/40 text-muted-foreground">
                        <IconExpand className="h-5 w-5" />
                      </div>
                      <p className="text-sm font-medium">{selectedStudent ? t("remoteViewNoFrame") : t("selectStudentStream")}</p>
                      <p className="mt-1 text-xs text-muted-foreground">{t("remoteViewPrepHint")}</p>
                    </div>
                  </div>
                )}
              </div>
            </div>
          </div>
        </div>
      ) : null}

      {isThumbnailsExpanded ? (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-3 sm:p-4">
          <button
            type="button"
            className="absolute inset-0 bg-black/60"
            aria-label="Close device thumbnails"
            onClick={() => setIsThumbnailsExpanded(false)}
          />
          <Card className="relative z-10 flex h-[min(90vh,860px)] w-full max-w-7xl flex-col overflow-hidden">
            <CardHeader className="pb-2">
              <div className="flex items-center justify-between gap-2">
                <CardTitle>{t("thumbnailsModalTitle")}</CardTitle>
                <Button variant="outline" size="sm" onClick={() => setIsThumbnailsExpanded(false)}>
                  {t("close")}
                </Button>
              </div>
              <CardDescription>{t("thumbnailsModalDesc")}</CardDescription>
            </CardHeader>
            <CardContent className="min-h-0 flex-1 pb-4">
              <ScrollArea.Root className="h-full overflow-hidden">
                <ScrollArea.Viewport className="h-full pr-2">
                  <div className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5">
                    {sortedStudents.map((student) => {
                      const frame = frames[student.clientId];
                      const isFocused = selectedStudentId === student.clientId;
                      const isTarget = selectedTargets.has(student.clientId);
                      const isHandRaised = (handRaisedUntil[student.clientId] ?? 0) > uiClock;
                      const unreadCount = chatUnreadByStudent[student.clientId] ?? 0;

                      return (
                        <div
                          key={`expanded-${student.clientId}`}
                          className={cn(
                            "rounded-md border p-2 transition hover:bg-accent/60 cursor-pointer",
                            isFocused ? "border-primary bg-primary/10" : "border-border",
                            unreadCount > 0 && !isFocused && "border-sky-500/40 bg-sky-500/5",
                          )}
                          onClick={() => setSelectedStudentId(student.clientId)}
                        >
                          <div className="mb-2 h-28 overflow-hidden rounded border border-border bg-muted/40">
                            {frame ? (
                              <img src={frame.url} alt={student.hostName} className="h-full w-full object-cover" />
                            ) : (
                              <div className="flex h-full items-center justify-center text-xs text-muted-foreground">{t("noFrame")}</div>
                            )}
                          </div>
                          <div className="flex items-center justify-between gap-2">
                            <div className="flex min-w-0 items-center gap-1">
                              <p className="truncate text-xs font-medium">{student.hostName}</p>
                              {unreadCount > 0 ? (
                                <Badge variant="outline" className="h-4 border-sky-500/40 bg-sky-500/10 px-1 text-[9px] text-sky-300">
                                  {unreadCount}
                                </Badge>
                              ) : null}
                            </div>
                            <Badge variant={student.isOnline ? "success" : "outline"} className="text-[10px]">
                              {student.isOnline ? t("online") : t("offline")}
                            </Badge>
                          </div>
                          <p className="truncate text-[11px] text-muted-foreground">{student.userName}</p>
                          <p className="truncate text-[11px] text-muted-foreground">
                            {frame ? new Date(frame.capturedAtUtc).toLocaleTimeString() : t("noUpdates")}
                          </p>
                          <div className="mt-1 flex items-center justify-between gap-2">
                            <span className="truncate text-[11px] text-muted-foreground">
                              {t("studentAlertsCount")}: {student.alertCount ?? 0}
                            </span>
                            {isHandRaised ? (
                              <Badge variant="warning" className="h-4 px-1 text-[9px]">
                                {t("handRaised")}
                              </Badge>
                            ) : null}
                          </div>
                          <div className="mt-1.5 flex items-center justify-between gap-2">
                            <span className="truncate text-[11px] text-muted-foreground">
                              {student.lastDetectionAtUtc
                                ? `${t("studentLastDetection")}: ${new Date(student.lastDetectionAtUtc).toLocaleTimeString()}`
                                : t("noUpdates")}
                            </span>
                            <label
                              className="inline-flex items-center gap-1 rounded border border-border px-1.5 py-0.5 text-[11px] text-muted-foreground"
                              onClick={(event) => event.stopPropagation()}
                            >
                              <input
                                type="checkbox"
                                checked={isTarget}
                                onChange={() => toggleTarget(student.clientId)}
                              />
                              {t("target")}
                            </label>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                  {sortedStudents.length === 0 ? (
                    <p className="mt-2 text-sm text-muted-foreground">{t("noMatches")}</p>
                  ) : null}
                </ScrollArea.Viewport>
                <ScrollArea.Scrollbar className="w-2" orientation="vertical">
                  <ScrollArea.Thumb className="rounded bg-muted-foreground/30" />
                </ScrollArea.Scrollbar>
              </ScrollArea.Root>
            </CardContent>
          </Card>
        </div>
      ) : null}

      {isTeacherChatModalOpen ? (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-3 sm:p-4">
          <button
            type="button"
            className="absolute inset-0 bg-black/55"
            aria-label="Close teacher chat dialog"
            onClick={() => setIsTeacherChatModalOpen(false)}
          />
          <Card className="relative z-10 flex h-[min(88vh,820px)] w-full max-w-4xl flex-col overflow-hidden">
            <CardHeader className="pb-2">
              <div className="flex items-center justify-between gap-2">
                <CardTitle>Teacher Chat</CardTitle>
                <Button variant="outline" size="sm" onClick={() => setIsTeacherChatModalOpen(false)}>
                  {t("close")}
                </Button>
              </div>
              <CardDescription>Chat with the selected student in an overlay window.</CardDescription>
            </CardHeader>
            <CardContent className="min-h-0 flex-1 overflow-auto pb-4">
              <FocusedChatCard
                selectedStudent={selectedStudent}
                messages={selectedChatMessages}
                isLoadingHistory={chatHistoryLoadingFor === selectedStudentId}
                isSending={sendTeacherChat.isPending}
                statusText={teacherChatStatus?.text}
                statusTone={teacherChatStatus?.tone}
                onSend={handleFocusedTeacherChatSend}
              />
            </CardContent>
          </Card>
        </div>
      ) : null}

      {isTeacherTtsModalOpen ? (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-3 sm:p-4">
          <button
            type="button"
            className="absolute inset-0 bg-black/55"
            aria-label="Close teacher TTS dialog"
            onClick={() => setIsTeacherTtsModalOpen(false)}
          />
          <Card className="relative z-10 flex h-[min(88vh,820px)] w-full max-w-3xl flex-col overflow-hidden">
            <CardHeader className="pb-2">
              <div className="flex items-center justify-between gap-2">
                <CardTitle>TTS</CardTitle>
                <Button variant="outline" size="sm" onClick={() => setIsTeacherTtsModalOpen(false)}>
                  {t("close")}
                </Button>
              </div>
              <CardDescription>Send a synthesized voice message to the selected student.</CardDescription>
            </CardHeader>
            <CardContent className="min-h-0 flex-1 overflow-auto pb-4">
              <TeacherTtsCard
                lang={lang}
                selectedStudent={selectedStudent}
                isPending={sendTeacherTts.isPending}
                statusText={teacherTtsStatus?.text}
                statusTone={teacherTtsStatus?.tone}
                onSend={handleTeacherTtsSend}
              />
            </CardContent>
          </Card>
        </div>
      ) : null}

      {isLogsOpen ? (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-3 sm:p-4">
          <button
            type="button"
            className="absolute inset-0 bg-black/55"
            aria-label="Close logs"
            onClick={() => setIsLogsOpen(false)}
          />
          <Card className="relative z-10 flex h-[min(88vh,760px)] w-full max-w-5xl flex-col overflow-hidden">
            <CardHeader className="pb-2">
              <div className="flex items-center justify-between gap-2">
                <CardTitle>{t("alertsAudit")}</CardTitle>
                <Button variant="outline" size="sm" onClick={() => setIsLogsOpen(false)}>
                  {t("close")}
                </Button>
              </div>
              <CardDescription>{t("alertsAuditDesc")}</CardDescription>
            </CardHeader>
            <CardContent className="min-h-0 flex-1 pb-4">
              <ScrollArea.Root className="h-full overflow-hidden">
                <ScrollArea.Viewport className="h-full pr-2">
                  <div className="space-y-2">
                    {events.length === 0 ? <p className="text-sm text-muted-foreground">{t("noEvents")}</p> : null}
                    {events.map((line, index) => (
                      <div
                        key={`${line}-${index}`}
                        className="rounded-md border border-border bg-background/80 px-3 py-2 text-xs leading-relaxed"
                      >
                        {humanizeProgramNames(line)}
                      </div>
                    ))}
                  </div>
                </ScrollArea.Viewport>
                <ScrollArea.Scrollbar className="w-2" orientation="vertical">
                  <ScrollArea.Thumb className="rounded bg-muted-foreground/30" />
                </ScrollArea.Scrollbar>
              </ScrollArea.Root>
            </CardContent>
          </Card>
        </div>
      ) : null}

      {isSendModalOpen ? (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-3 sm:p-4">
          <button
            type="button"
            className="absolute inset-0 bg-black/55"
            aria-label="Close send files dialog"
            onClick={() => setIsSendModalOpen(false)}
          />
          <Card className="relative z-10 w-full max-w-xl">
            <CardHeader className="pb-3">
              <div className="flex items-center justify-between gap-2">
                <CardTitle>{t("sendFilesModalTitle")}</CardTitle>
                <Button variant="outline" size="sm" onClick={() => setIsSendModalOpen(false)}>
                  {t("close")}
                </Button>
              </div>
              <CardDescription>{t("sendFilesModalDesc")}</CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              <input
                ref={fileInputRef}
                type="file"
                className="hidden"
                onChange={(event) => setFile(event.target.files?.[0] ?? null)}
              />

              <div className="rounded-lg border border-border bg-background/70 p-3">
                <div className="flex flex-wrap items-center gap-2">
                  <Button variant="outline" onClick={openFileDialog}>
                    {t("chooseFile")}
                  </Button>
                  {file ? (
                    <Button variant="ghost" size="sm" onClick={() => setFile(null)}>
                      {t("clearFile")}
                    </Button>
                  ) : null}
                </div>
                <p className="mt-2 text-sm text-muted-foreground">{file ? file.name : t("fileNotSelected")}</p>
              </div>

              <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                <Button onClick={sendToAll} disabled={!file || sendFile.isPending}>
                  {t("sendToAll")}
                </Button>
                <Button variant="secondary" onClick={sendToSelected} disabled={!file || sendFile.isPending}>
                  {t("sendToSelected")}
                </Button>
              </div>

              <p className="text-xs text-muted-foreground">{uploadStatus || t("noActiveTransfers")}</p>
            </CardContent>
          </Card>
        </div>
      ) : null}
    </div>
  );
}

export default App;





