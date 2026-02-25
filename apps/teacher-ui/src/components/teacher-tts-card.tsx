import { useState } from "react";
import { UiLanguage } from "../i18n";
import { StudentInfo } from "../lib/types";
import { cn } from "../lib/utils";
import { Badge } from "./ui/badge";
import { Button } from "./ui/button";

type StatusTone = "neutral" | "success" | "error";

export type TeacherTtsDraft = {
  messageText: string;
  languageCode?: string;
  voiceName?: string;
  speakingRate?: number;
  pitch?: number;
};

type Props = {
  lang: UiLanguage;
  selectedStudent?: StudentInfo;
  isPending?: boolean;
  statusText?: string;
  statusTone?: StatusTone;
  onSend: (draft: TeacherTtsDraft) => void;
};

const copy: Record<UiLanguage, Record<string, string>> = {
  ru: {
    title: "Озвучить текст ученику (TTS)",
    hint: "Отправьте короткий текст. Озвучивание выполнит Student.Agent через облачный TTS.",
    noStudent: "Выберите устройство",
    offline: "Устройство оффлайн",
    message: "Текст для озвучивания",
    placeholder: "Например: Начинаем задание номер 3. Если нужна помощь, поднимите руку.",
    language: "Язык",
    voice: "Голос (необязательно)",
    rate: "Скорость",
    pitch: "Тон",
    send: "Отправить TTS",
    sending: "Отправка...",
    presetRu: "Русский",
    presetEn: "English",
    presetKz: "Қазақша",
  },
  en: {
    title: "Speak Text To Student (TTS)",
    hint: "Send a short message. Student.Agent will synthesize it via cloud TTS.",
    noStudent: "Select a device",
    offline: "Device offline",
    message: "Text to speak",
    placeholder: "Example: Please open task number 3. Raise your hand if you need help.",
    language: "Language",
    voice: "Voice (optional)",
    rate: "Rate",
    pitch: "Pitch",
    send: "Send TTS",
    sending: "Sending...",
    presetRu: "Russian",
    presetEn: "English",
    presetKz: "Kazakh",
  },
  kz: {
    title: "Студентке мәтінді дыбыстау (TTS)",
    hint: "Қысқа мәтін жіберіңіз. Student.Agent оны бұлттық TTS арқылы дыбыстайды.",
    noStudent: "Құрылғыны таңдаңыз",
    offline: "Құрылғы оффлайн",
    message: "Дыбысталатын мәтін",
    placeholder: "Мысалы: 3-тапсырманы ашыңыз. Көмек керек болса, қол көтеріңіз.",
    language: "Тіл",
    voice: "Дауыс (міндетті емес)",
    rate: "Жылдамдық",
    pitch: "Тон",
    send: "TTS жіберу",
    sending: "Жіберілуде...",
    presetRu: "Орысша",
    presetEn: "Ағылшынша",
    presetKz: "Қазақша",
  },
};

function t(lang: UiLanguage, key: string): string {
  return copy[lang]?.[key] ?? copy.en[key] ?? key;
}

export function TeacherTtsCard({
  lang,
  selectedStudent,
  isPending = false,
  statusText,
  statusTone = "neutral",
  onSend,
}: Props) {
  const [messageText, setMessageText] = useState("");
  const [languageCode, setLanguageCode] = useState("ru-RU");
  const [voiceName, setVoiceName] = useState("");
  const [speakingRate, setSpeakingRate] = useState(1);
  const [pitch, setPitch] = useState(0);

  const canSend = Boolean(selectedStudent?.isOnline) && messageText.trim().length > 0 && !isPending;

  return (
    <div className="rounded-lg border border-border bg-background/70 p-3">
      <div className="mb-2 flex flex-wrap items-start justify-between gap-2">
        <div>
          <p className="text-xs uppercase tracking-[0.11em] text-muted-foreground">{t(lang, "title")}</p>
          <p className="mt-1 text-xs text-muted-foreground">{t(lang, "hint")}</p>
        </div>
        {selectedStudent ? (
          <Badge variant={selectedStudent.isOnline ? "success" : "outline"}>{selectedStudent.hostName}</Badge>
        ) : (
          <Badge variant="outline">{t(lang, "noStudent")}</Badge>
        )}
      </div>

      <div className="space-y-2">
        <label className="block">
          <span className="mb-1 block text-[11px] text-muted-foreground">{t(lang, "message")}</span>
          <textarea
            className="min-h-[86px] w-full rounded-md border border-input bg-background px-2.5 py-2 text-sm outline-none ring-offset-background focus-visible:ring-2 focus-visible:ring-primary"
            value={messageText}
            onChange={(event) => setMessageText(event.target.value)}
            placeholder={t(lang, "placeholder")}
            maxLength={600}
            disabled={isPending}
          />
          <div className="mt-1 flex justify-end text-[10px] text-muted-foreground">{messageText.length}/600</div>
        </label>

        <div className="grid grid-cols-2 gap-2">
          <label className="block">
            <span className="mb-1 block text-[11px] text-muted-foreground">{t(lang, "language")}</span>
            <select
              className="h-9 w-full rounded-md border border-input bg-background px-2 text-xs"
              value={languageCode}
              onChange={(event) => setLanguageCode(event.target.value)}
              disabled={isPending}
            >
              <option value="ru-RU">{t(lang, "presetRu")} (ru-RU)</option>
              <option value="en-US">{t(lang, "presetEn")} (en-US)</option>
              <option value="kk-KZ">{t(lang, "presetKz")} (kk-KZ)</option>
            </select>
          </label>

          <label className="block">
            <span className="mb-1 block text-[11px] text-muted-foreground">{t(lang, "voice")}</span>
            <input
              className="h-9 w-full rounded-md border border-input bg-background px-2 text-xs"
              value={voiceName}
              onChange={(event) => setVoiceName(event.target.value)}
              placeholder="ru-RU-Wavenet-A"
              disabled={isPending}
            />
          </label>
        </div>

        <div className="grid grid-cols-2 gap-2">
          <label className="block rounded-md border border-border/80 bg-muted/20 px-2 py-1.5">
            <div className="mb-1 flex items-center justify-between text-[11px] text-muted-foreground">
              <span>{t(lang, "rate")}</span>
              <span>{speakingRate.toFixed(2)}</span>
            </div>
            <input
              className="slider-fancy w-full"
              type="range"
              min={0.5}
              max={1.5}
              step={0.05}
              value={speakingRate}
              onChange={(event) => setSpeakingRate(Number(event.target.value))}
              disabled={isPending}
            />
          </label>

          <label className="block rounded-md border border-border/80 bg-muted/20 px-2 py-1.5">
            <div className="mb-1 flex items-center justify-between text-[11px] text-muted-foreground">
              <span>{t(lang, "pitch")}</span>
              <span>{pitch.toFixed(1)}</span>
            </div>
            <input
              className="slider-fancy w-full"
              type="range"
              min={-6}
              max={6}
              step={0.5}
              value={pitch}
              onChange={(event) => setPitch(Number(event.target.value))}
              disabled={isPending}
            />
          </label>
        </div>

        {statusText ? (
          <div
            className={cn(
              "rounded-md border px-2 py-1.5 text-xs",
              statusTone === "success" && "border-emerald-500/40 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300",
              statusTone === "error" && "border-destructive/40 bg-destructive/10 text-destructive",
              statusTone === "neutral" && "border-border bg-muted/25 text-muted-foreground",
            )}
          >
            {statusText}
          </div>
        ) : null}

        <Button
          className="w-full"
          size="sm"
          disabled={!canSend}
          onClick={() =>
            onSend({
              messageText: messageText.trim(),
              languageCode,
              voiceName: voiceName.trim() || undefined,
              speakingRate,
              pitch,
            })
          }
        >
          {!selectedStudent ? t(lang, "noStudent") : !selectedStudent.isOnline ? t(lang, "offline") : isPending ? t(lang, "sending") : t(lang, "send")}
        </Button>
      </div>
    </div>
  );
}


