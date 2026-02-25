import { useState } from "react";
import { UiLanguage } from "../i18n";
import {
  AccessibilityContrastMode,
  AccessibilityPresetId,
  AccessibilityProfileUpdateDto,
  StudentInfo,
} from "../lib/types";
import { cn } from "../lib/utils";
import { Badge } from "./ui/badge";
import { Button } from "./ui/button";

type StatusTone = "neutral" | "success" | "error";

type Props = {
  lang: UiLanguage;
  selectedStudent?: StudentInfo;
  isPending?: boolean;
  statusText?: string;
  statusTone?: StatusTone;
  onAssign: (profile: AccessibilityProfileUpdateDto) => void;
};

const presetOrder: AccessibilityPresetId[] = ["default", "vision", "hearing", "motor", "dyslexia", "custom"];

const copy: Record<UiLanguage, Record<string, string>> = {
  ru: {
    title: "Профиль доступности",
    hint: "Назначьте пресет или кастомный набор выбранному ученику удалённо.",
    noStudent: "Выберите устройство",
    offline: "Устройство оффлайн",
    presets: "Пресеты",
    ui: "Интерфейс",
    lesson: "Функции урока",
    scale: "Масштаб",
    contrast: "Контраст",
    invert: "Инверсия",
    dyslexiaFont: "Шрифт для чтения",
    cursor: "Большой курсор",
    focus: "Подсветка фокуса",
    visualAlerts: "Визуальные сигналы",
    largeButtons: "Крупные кнопки",
    simpleNav: "Упрощённая навигация",
    captions: "Субтитры",
    tts: "TTS сообщений",
    voiceCmd: "Голосовые команды",
    teacherOverride: "Разрешить последующие teacher overrides",
    apply: "Применить профайл",
    applying: "Отправка...",
    standard: "Стандарт",
    vision: "Зрение",
    hearing: "Слух",
    motor: "Моторика",
    dyslexia: "Дислексия/СДВГ",
    custom: "Кастом",
    contrastStandard: "Обычный",
    contrastAa: "Высокий AA",
    contrastAaa: "Высокий AAA",
  },
  en: {
    title: "Accessibility Profile",
    hint: "Assign a preset or custom accessibility profile to the selected student remotely.",
    noStudent: "Select a device",
    offline: "Device offline",
    presets: "Presets",
    ui: "Interface",
    lesson: "Lesson features",
    scale: "Scale",
    contrast: "Contrast",
    invert: "Invert",
    dyslexiaFont: "Reading font",
    cursor: "Large cursor",
    focus: "Focus highlight",
    visualAlerts: "Visual alerts",
    largeButtons: "Large buttons",
    simpleNav: "Simplified nav",
    captions: "Live captions",
    tts: "Message TTS",
    voiceCmd: "Voice commands",
    teacherOverride: "Allow future teacher overrides",
    apply: "Apply profile",
    applying: "Sending...",
    standard: "Standard",
    vision: "Vision",
    hearing: "Hearing",
    motor: "Motor",
    dyslexia: "Dyslexia/ADHD",
    custom: "Custom",
    contrastStandard: "Standard",
    contrastAa: "High AA",
    contrastAaa: "High AAA",
  },
  kz: {
    title: "Қолжетімділік профилі",
    hint: "Таңдалған студентке пресет не кастом профильді қашықтан тағайындаңыз.",
    noStudent: "Құрылғыны таңдаңыз",
    offline: "Құрылғы оффлайн",
    presets: "Пресеттер",
    ui: "Интерфейс",
    lesson: "Сабақ функциялары",
    scale: "Масштаб",
    contrast: "Контраст",
    invert: "Инверсия",
    dyslexiaFont: "Оқу қарпі",
    cursor: "Үлкен курсор",
    focus: "Фокус белгілеу",
    visualAlerts: "Визуалды сигналдар",
    largeButtons: "Ірі батырмалар",
    simpleNav: "Қарапайым навигация",
    captions: "Субтитр",
    tts: "Хабар TTS",
    voiceCmd: "Дауыс командалары",
    teacherOverride: "Кейінгі teacher override-қа рұқсат",
    apply: "Профильді қолдану",
    applying: "Жіберілуде...",
    standard: "Стандарт",
    vision: "Көру",
    hearing: "Есту",
    motor: "Моторика",
    dyslexia: "Дислексия/СДВГ",
    custom: "Кастом",
    contrastStandard: "Қалыпты",
    contrastAa: "Жоғары AA",
    contrastAaa: "Жоғары AAA",
  },
};

function t(lang: UiLanguage, key: string): string {
  return copy[lang]?.[key] ?? copy.en[key] ?? key;
}

function presetLabel(lang: UiLanguage, preset: AccessibilityPresetId): string {
  return t(lang, preset === "default" ? "standard" : preset);
}

function createPreset(preset: AccessibilityPresetId): AccessibilityProfileUpdateDto {
  switch (preset) {
    case "vision":
      return {
        activePreset: "vision",
        allowTeacherOverride: true,
        ui: {
          scalePercent: 150,
          contrastMode: "aaa",
          invertColors: false,
          colorBlindMode: "none",
          dyslexiaFontEnabled: false,
          largeCursorEnabled: true,
          highlightFocusEnabled: true,
        },
        features: {
          visualAlertsEnabled: true,
          largeActionButtonsEnabled: true,
          simplifiedNavigationEnabled: true,
          singleKeyModeEnabled: false,
          ttsTeacherMessagesEnabled: true,
          audioLessonModeEnabled: false,
          liveCaptionsEnabled: false,
          voiceCommandsEnabled: false,
        },
      };
    case "hearing":
      return {
        activePreset: "hearing",
        allowTeacherOverride: true,
        ui: {
          scalePercent: 115,
          contrastMode: "aa",
          invertColors: false,
          colorBlindMode: "none",
          dyslexiaFontEnabled: false,
          largeCursorEnabled: false,
          highlightFocusEnabled: true,
        },
        features: {
          visualAlertsEnabled: true,
          largeActionButtonsEnabled: false,
          simplifiedNavigationEnabled: false,
          singleKeyModeEnabled: false,
          ttsTeacherMessagesEnabled: false,
          audioLessonModeEnabled: false,
          liveCaptionsEnabled: true,
          voiceCommandsEnabled: false,
        },
      };
    case "motor":
      return {
        activePreset: "motor",
        allowTeacherOverride: true,
        ui: {
          scalePercent: 130,
          contrastMode: "aa",
          invertColors: false,
          colorBlindMode: "none",
          dyslexiaFontEnabled: false,
          largeCursorEnabled: true,
          highlightFocusEnabled: true,
        },
        features: {
          visualAlertsEnabled: true,
          largeActionButtonsEnabled: true,
          simplifiedNavigationEnabled: true,
          singleKeyModeEnabled: true,
          ttsTeacherMessagesEnabled: false,
          audioLessonModeEnabled: false,
          liveCaptionsEnabled: false,
          voiceCommandsEnabled: false,
        },
      };
    case "dyslexia":
      return {
        activePreset: "dyslexia",
        allowTeacherOverride: true,
        ui: {
          scalePercent: 115,
          contrastMode: "aa",
          invertColors: false,
          colorBlindMode: "none",
          dyslexiaFontEnabled: true,
          largeCursorEnabled: false,
          highlightFocusEnabled: true,
        },
        features: {
          visualAlertsEnabled: true,
          largeActionButtonsEnabled: false,
          simplifiedNavigationEnabled: false,
          singleKeyModeEnabled: false,
          ttsTeacherMessagesEnabled: false,
          audioLessonModeEnabled: false,
          liveCaptionsEnabled: false,
          voiceCommandsEnabled: false,
        },
      };
    case "custom":
      return {
        ...createPreset("default"),
        activePreset: "custom",
      };
    default:
      return {
        activePreset: "default",
        allowTeacherOverride: true,
        ui: {
          scalePercent: 100,
          contrastMode: "standard",
          invertColors: false,
          colorBlindMode: "none",
          dyslexiaFontEnabled: false,
          largeCursorEnabled: false,
          highlightFocusEnabled: false,
        },
        features: {
          visualAlertsEnabled: true,
          largeActionButtonsEnabled: false,
          simplifiedNavigationEnabled: false,
          singleKeyModeEnabled: false,
          ttsTeacherMessagesEnabled: false,
          audioLessonModeEnabled: false,
          liveCaptionsEnabled: false,
          voiceCommandsEnabled: false,
        },
      };
  }
}

export function AccessibilityAssignmentCard({
  lang,
  selectedStudent,
  isPending = false,
  statusText,
  statusTone = "neutral",
  onAssign,
}: Props) {
  const [draft, setDraft] = useState<AccessibilityProfileUpdateDto>(() => createPreset("default"));

  const applyPreset = (preset: AccessibilityPresetId) => {
    setDraft(createPreset(preset));
  };

  const patchDraft = (patch: Partial<AccessibilityProfileUpdateDto>) => {
    setDraft((current) => ({
      ...current,
      ...patch,
      activePreset: "custom",
    }));
  };

  const patchUi = (patch: Partial<AccessibilityProfileUpdateDto["ui"]>) => {
    setDraft((current) => ({
      ...current,
      activePreset: "custom",
      ui: { ...current.ui, ...patch },
    }));
  };

  const patchFeatures = (patch: Partial<AccessibilityProfileUpdateDto["features"]>) => {
    setDraft((current) => ({
      ...current,
      activePreset: "custom",
      features: { ...current.features, ...patch },
    }));
  };

  const canSubmit = Boolean(selectedStudent?.isOnline) && !isPending;

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
        <div>
          <p className="mb-1 text-[11px] font-medium uppercase tracking-[0.08em] text-muted-foreground">{t(lang, "presets")}</p>
          <div className="grid grid-cols-2 gap-1.5">
            {presetOrder.map((preset) => (
              <Button
                key={preset}
                size="sm"
                variant={draft.activePreset === preset ? "default" : "outline"}
                className="h-8 justify-start px-2 text-[11px]"
                onClick={() => applyPreset(preset)}
                disabled={isPending}
              >
                {presetLabel(lang, preset)}
              </Button>
            ))}
          </div>
        </div>

        <div className="grid grid-cols-2 gap-2">
          <label className="space-y-1">
            <span className="text-[11px] text-muted-foreground">{t(lang, "scale")}</span>
            <div className="rounded border border-border bg-background/60 px-2 py-1.5">
              <div className="mb-1 flex items-center justify-between text-[11px]">
                <span>{draft.ui.scalePercent}%</span>
              </div>
              <input
                type="range"
                min={100}
                max={300}
                step={5}
                className="w-full accent-primary"
                value={draft.ui.scalePercent}
                onChange={(event) => patchUi({ scalePercent: Number(event.target.value) })}
                disabled={isPending}
              />
            </div>
          </label>

          <label className="space-y-1">
            <span className="text-[11px] text-muted-foreground">{t(lang, "contrast")}</span>
            <select
              className="h-[56px] w-full rounded-md border border-input bg-background px-2 text-xs"
              value={draft.ui.contrastMode}
              onChange={(event) => patchUi({ contrastMode: event.target.value as AccessibilityContrastMode })}
              disabled={isPending}
            >
              <option value="standard">{t(lang, "contrastStandard")}</option>
              <option value="aa">{t(lang, "contrastAa")}</option>
              <option value="aaa">{t(lang, "contrastAaa")}</option>
            </select>
          </label>
        </div>

        <div className="grid gap-1.5">
          <SwitchRow
            label={t(lang, "teacherOverride")}
            checked={draft.allowTeacherOverride}
            disabled={isPending}
            onChange={(checked) => patchDraft({ allowTeacherOverride: checked })}
          />
          <div className="grid grid-cols-2 gap-1.5">
            <SwitchRow
              compact
              label={t(lang, "invert")}
              checked={draft.ui.invertColors}
              disabled={isPending}
              onChange={(checked) => patchUi({ invertColors: checked })}
            />
            <SwitchRow
              compact
              label={t(lang, "dyslexiaFont")}
              checked={draft.ui.dyslexiaFontEnabled}
              disabled={isPending}
              onChange={(checked) => patchUi({ dyslexiaFontEnabled: checked })}
            />
            <SwitchRow
              compact
              label={t(lang, "cursor")}
              checked={draft.ui.largeCursorEnabled}
              disabled={isPending}
              onChange={(checked) => patchUi({ largeCursorEnabled: checked })}
            />
            <SwitchRow
              compact
              label={t(lang, "focus")}
              checked={draft.ui.highlightFocusEnabled}
              disabled={isPending}
              onChange={(checked) => patchUi({ highlightFocusEnabled: checked })}
            />
          </div>
        </div>

        <div>
          <p className="mb-1 text-[11px] font-medium uppercase tracking-[0.08em] text-muted-foreground">{t(lang, "lesson")}</p>
          <div className="grid grid-cols-2 gap-1.5">
            <SwitchRow compact label={t(lang, "visualAlerts")} checked={draft.features.visualAlertsEnabled} disabled={isPending} onChange={(checked) => patchFeatures({ visualAlertsEnabled: checked })} />
            <SwitchRow compact label={t(lang, "largeButtons")} checked={draft.features.largeActionButtonsEnabled} disabled={isPending} onChange={(checked) => patchFeatures({ largeActionButtonsEnabled: checked })} />
            <SwitchRow compact label={t(lang, "simpleNav")} checked={draft.features.simplifiedNavigationEnabled} disabled={isPending} onChange={(checked) => patchFeatures({ simplifiedNavigationEnabled: checked })} />
            <SwitchRow compact label={t(lang, "captions")} checked={draft.features.liveCaptionsEnabled} disabled={isPending} onChange={(checked) => patchFeatures({ liveCaptionsEnabled: checked })} />
            <SwitchRow compact label={t(lang, "tts")} checked={draft.features.ttsTeacherMessagesEnabled} disabled={isPending} onChange={(checked) => patchFeatures({ ttsTeacherMessagesEnabled: checked })} />
            <SwitchRow compact label={t(lang, "voiceCmd")} checked={draft.features.voiceCommandsEnabled} disabled={isPending} onChange={(checked) => patchFeatures({ voiceCommandsEnabled: checked })} />
          </div>
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
          disabled={!canSubmit}
          onClick={() => onAssign(draft)}
        >
          {!selectedStudent ? t(lang, "noStudent") : !selectedStudent.isOnline ? t(lang, "offline") : isPending ? t(lang, "applying") : t(lang, "apply")}
        </Button>
      </div>
    </div>
  );
}

type SwitchRowProps = {
  label: string;
  checked: boolean;
  disabled?: boolean;
  compact?: boolean;
  onChange: (checked: boolean) => void;
};

function SwitchRow({ label, checked, disabled, compact = false, onChange }: SwitchRowProps) {
  return (
    <label
      className={cn(
        "flex items-center justify-between gap-2 rounded border border-border/80 bg-background/55 text-muted-foreground",
        compact ? "px-2 py-1.5 text-[11px]" : "px-2.5 py-2 text-xs",
      )}
    >
      <span className={cn("leading-tight", checked && "text-foreground")}>{label}</span>
      <input
        type="checkbox"
        className="h-3.5 w-3.5 accent-primary"
        checked={checked}
        onChange={(event) => onChange(event.target.checked)}
        disabled={disabled}
      />
    </label>
  );
}

