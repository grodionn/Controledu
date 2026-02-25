import { useEffect, useState } from "react";
import { Badge } from "./ui/badge";
import { Button } from "./ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "./ui/card";
import {
  AccessibilityColorBlindMode,
  AccessibilityContrastMode,
  AccessibilityFeatureFlags,
  AccessibilityPresetId,
  AccessibilityProfileResponse,
  AccessibilityProfileUpdateRequest,
  AccessibilityUiSettings,
} from "../types";
import { UiLanguage } from "../i18n";
import { cn } from "../lib/utils";

type ApiClient = <T>(path: string, init?: RequestInit) => Promise<T>;
type MessageTone = "neutral" | "success" | "error";

type Props = {
  api: ApiClient;
  lang: UiLanguage;
  disabled?: boolean;
  onStatusMessage?: (text: string, tone?: MessageTone) => void;
};

const presetOrder: AccessibilityPresetId[] = ["default", "vision", "hearing", "motor", "dyslexia", "custom"];

const presetLabels: Record<UiLanguage, Record<AccessibilityPresetId, string>> = {
  ru: {
    default: "Стандарт",
    vision: "Зрение",
    hearing: "Слух",
    motor: "Моторика",
    dyslexia: "Дислексия/СДВГ",
    custom: "Кастомный",
  },
  en: {
    default: "Standard",
    vision: "Vision",
    hearing: "Hearing",
    motor: "Motor",
    dyslexia: "Dyslexia/ADHD",
    custom: "Custom",
  },
  kz: {
    default: "Стандарт",
    vision: "Көру",
    hearing: "Есту",
    motor: "Моторика",
    dyslexia: "Дислексия/СДВГ",
    custom: "Кастом",
  },
};

const textMap: Record<UiLanguage, Record<string, string>> = {
  ru: {
    title: "Доступность и персональный профиль",
    description: "Быстрые пресеты + кастомный профиль для этого компьютера. Профиль хранится локально и может быть назначен учителем позже.",
    presets: "Профили в 1 клик",
    localCustom: "Кастомный профиль этого ПК",
    interface: "Интерфейс",
    features: "Функции урока",
    scale: "Масштаб интерфейса",
    contrast: "Контраст",
    invert: "Инверсия цветов",
    colorMode: "Режим дальтонизма",
    dyslexiaFont: "Шрифт для чтения (OpenDyslexic fallback)",
    largeCursor: "Увеличенный курсор (UI-режим)",
    focusHighlight: "Подсветка активного элемента",
    visualAlerts: "Визуальные уведомления",
    largeButtons: "Крупные кнопки действий",
    simpleNav: "Упрощённая навигация",
    singleKey: "Управление одной клавишей (след. этап)",
    tts: "Озвучивание сообщений учителя (TTS, след. этап)",
    audioLesson: "Аудио-режим урока (след. этап)",
    liveCaptions: "Автосубтитры (след. этап)",
    voiceCommands: "Голосовые команды (след. этап)",
    teacherOverride: "Разрешить удалённое назначение профиля учителем",
    save: "Сохранить профиль",
    reset: "Сбросить черновик",
    saving: "Сохраняю...",
    loading: "Загрузка профиля доступности...",
    sourceLocal: "Изменён локально",
    sourceTeacher: "Назначен учителем",
    sourcePreset: "Применён пресет",
    lastUpdate: "Последнее обновление",
    assignedBy: "Назначил",
    unsaved: "Есть несохранённые изменения",
    saved: "Профиль доступности сохранён.",
    presetApplied: "Пресет доступности применён.",
    standardContrast: "Обычный",
    contrastAa: "Высокий (AA)",
    contrastAaa: "Высокий (AAA)",
    colorNone: "Нет",
    protanopia: "Протанопия",
    deuteranopia: "Дейтеранопия",
    tritanopia: "Тританопия",
  },
  en: {
    title: "Accessibility and Personal Profile",
    description: "One-click presets plus a custom profile for this device. Stored locally now; ready for teacher assignment integration.",
    presets: "One-click presets",
    localCustom: "Custom profile for this PC",
    interface: "Interface",
    features: "Lesson features",
    scale: "UI scale",
    contrast: "Contrast",
    invert: "Invert colors",
    colorMode: "Color blindness mode",
    dyslexiaFont: "Reading font (OpenDyslexic fallback)",
    largeCursor: "Large cursor (UI mode)",
    focusHighlight: "Highlight active control",
    visualAlerts: "Visual alerts",
    largeButtons: "Large action buttons",
    simpleNav: "Simplified navigation",
    singleKey: "Single-key control (next step)",
    tts: "Teacher message TTS (next step)",
    audioLesson: "Lesson audio mode (next step)",
    liveCaptions: "Live captions (next step)",
    voiceCommands: "Voice commands (next step)",
    teacherOverride: "Allow teacher to assign profile remotely",
    save: "Save profile",
    reset: "Reset draft",
    saving: "Saving...",
    loading: "Loading accessibility profile...",
    sourceLocal: "Updated locally",
    sourceTeacher: "Assigned by teacher",
    sourcePreset: "Preset applied",
    lastUpdate: "Last update",
    assignedBy: "Assigned by",
    unsaved: "Unsaved changes",
    saved: "Accessibility profile saved.",
    presetApplied: "Accessibility preset applied.",
    standardContrast: "Standard",
    contrastAa: "High (AA)",
    contrastAaa: "High (AAA)",
    colorNone: "None",
    protanopia: "Protanopia",
    deuteranopia: "Deuteranopia",
    tritanopia: "Tritanopia",
  },
  kz: {
    title: "Қолжетімділік және жеке профиль",
    description: "Бір батырмалы пресеттер және осы компьютерге арналған кастом профиль.",
    presets: "Бір батырмалы пресеттер",
    localCustom: "Осы ПК үшін кастом профиль",
    interface: "Интерфейс",
    features: "Сабақ функциялары",
    scale: "Интерфейс масштабы",
    contrast: "Контраст",
    invert: "Түстерді инверсиялау",
    colorMode: "Дальтонизм режимі",
    dyslexiaFont: "Оқу қарпі (fallback)",
    largeCursor: "Үлкен курсор (UI)",
    focusHighlight: "Белсенді элементті белгілеу",
    visualAlerts: "Визуалды хабарламалар",
    largeButtons: "Ірі әрекет батырмалары",
    simpleNav: "Қарапайым навигация",
    singleKey: "Бір пернемен басқару (келесі кезең)",
    tts: "Мұғалім хабарын TTS (келесі кезең)",
    audioLesson: "Сабақ аудио режимі (келесі кезең)",
    liveCaptions: "Авто субтитр (келесі кезең)",
    voiceCommands: "Дауыс командалары (келесі кезең)",
    teacherOverride: "Мұғалімге профильді қашықтан тағайындауға рұқсат беру",
    save: "Профильді сақтау",
    reset: "Черновикті қалпына келтіру",
    saving: "Сақталуда...",
    loading: "Қолжетімділік профилі жүктелуде...",
    sourceLocal: "Жергілікті өзгертілді",
    sourceTeacher: "Мұғалім тағайындады",
    sourcePreset: "Пресет қолданылды",
    lastUpdate: "Соңғы жаңарту",
    assignedBy: "Тағайындаған",
    unsaved: "Сақталмаған өзгерістер бар",
    saved: "Қолжетімділік профилі сақталды.",
    presetApplied: "Қолжетімділік пресеті қолданылды.",
    standardContrast: "Қалыпты",
    contrastAa: "Жоғары (AA)",
    contrastAaa: "Жоғары (AAA)",
    colorNone: "Жоқ",
    protanopia: "Протанопия",
    deuteranopia: "Дейтеранопия",
    tritanopia: "Тританопия",
  },
};

function getCopy(lang: UiLanguage) {
  return textMap[lang] ?? textMap.ru;
}

function toDraft(profile: AccessibilityProfileResponse): AccessibilityProfileUpdateRequest {
  return {
    activePreset: profile.activePreset,
    allowTeacherOverride: profile.allowTeacherOverride,
    ui: { ...profile.ui },
    features: { ...profile.features },
  };
}

function isoToLocal(value?: string | null) {
  if (!value) {
    return "";
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function applyAccessibilityToDocument(profile: AccessibilityProfileUpdateRequest | null) {
  const root = document.documentElement;
  const body = document.body;

  if (!profile) {
    return;
  }

  const scale = Math.max(100, Math.min(300, profile.ui.scalePercent));
  root.style.fontSize = `${Math.round((16 * scale) / 100)}px`;
  root.dataset.a11yContrast = profile.ui.contrastMode;
  root.dataset.a11yColorblind = profile.ui.colorBlindMode;
  root.dataset.a11yInvert = String(profile.ui.invertColors);
  root.dataset.a11yDyslexiaFont = String(profile.ui.dyslexiaFontEnabled);
  root.dataset.a11yLargeCursor = String(profile.ui.largeCursorEnabled);
  root.dataset.a11yHighlightFocus = String(profile.ui.highlightFocusEnabled);
  root.dataset.a11yLargeButtons = String(profile.features.largeActionButtonsEnabled);
  root.dataset.a11ySimpleNav = String(profile.features.simplifiedNavigationEnabled);
  root.dataset.a11yVisualAlerts = String(profile.features.visualAlertsEnabled);

  body.dataset.a11yInvert = String(profile.ui.invertColors);

  const filters: string[] = [];
  if (profile.ui.invertColors) {
    filters.push("invert(1)", "hue-rotate(180deg)");
  }

  if (profile.ui.colorBlindMode === "protanopia") {
    filters.push("sepia(0.22)", "saturate(0.75)", "hue-rotate(-12deg)");
  } else if (profile.ui.colorBlindMode === "deuteranopia") {
    filters.push("sepia(0.15)", "saturate(0.7)", "hue-rotate(18deg)");
  } else if (profile.ui.colorBlindMode === "tritanopia") {
    filters.push("saturate(0.82)", "hue-rotate(35deg)");
  }

  body.style.filter = filters.length > 0 ? filters.join(" ") : "";
}

export function AccessibilitySettingsPanel({ api, lang, disabled = false, onStatusMessage }: Props) {
  const copy = getCopy(lang);
  const [profile, setProfile] = useState<AccessibilityProfileResponse | null>(null);
  const [draft, setDraft] = useState<AccessibilityProfileUpdateRequest | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string>("");

  useEffect(() => {
    let cancelled = false;

    setIsLoading(true);
    setError("");

    api<AccessibilityProfileResponse>("/api/accessibility/profile")
      .then((payload) => {
        if (cancelled) {
          return;
        }

        setProfile(payload);
        setDraft(toDraft(payload));
      })
      .catch((loadError) => {
        if (cancelled) {
          return;
        }

        const text = String(loadError);
        setError(text);
        onStatusMessage?.(text, "error");
      })
      .finally(() => {
        if (!cancelled) {
          setIsLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [api]);

  useEffect(() => {
    applyAccessibilityToDocument(draft);
  }, [draft]);

  if (isLoading && !draft) {
    return (
      <Card className="border-border/80 bg-card/90">
        <CardHeader>
          <CardTitle>{copy.title}</CardTitle>
          <CardDescription>{copy.loading}</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  if (!draft) {
    return null;
  }

  const isDirty = JSON.stringify(draft) !== JSON.stringify(profile ? toDraft(profile) : null);

  const setUi = <K extends keyof AccessibilityUiSettings>(key: K, value: AccessibilityUiSettings[K]) => {
    setDraft((current) =>
      current
        ? {
            ...current,
            activePreset: "custom",
            ui: {
              ...current.ui,
              [key]: value,
            },
          }
        : current,
    );
  };

  const setFeature = <K extends keyof AccessibilityFeatureFlags>(key: K, value: AccessibilityFeatureFlags[K]) => {
    setDraft((current) =>
      current
        ? {
            ...current,
            activePreset: "custom",
            features: {
              ...current.features,
              [key]: value,
            },
          }
        : current,
    );
  };

  const setAllowTeacherOverride = (value: boolean) => {
    setDraft((current) => (current ? { ...current, allowTeacherOverride: value } : current));
  };

  const applyPreset = async (presetId: AccessibilityPresetId) => {
    if (disabled || isSaving) {
      return;
    }

    if (presetId === "custom") {
      setDraft((current) => (current ? { ...current, activePreset: "custom" } : current));
      return;
    }

    setIsSaving(true);
    setError("");
    try {
      const payload = await api<AccessibilityProfileResponse>("/api/accessibility/profile/preset", {
        method: "POST",
        body: JSON.stringify({ presetId }),
      });
      setProfile(payload);
      setDraft(toDraft(payload));
      onStatusMessage?.(copy.presetApplied, "success");
    } catch (applyError) {
      const text = String(applyError);
      setError(text);
      onStatusMessage?.(text, "error");
    } finally {
      setIsSaving(false);
    }
  };

  const save = async () => {
    if (disabled || isSaving) {
      return;
    }

    setIsSaving(true);
    setError("");
    try {
      const payload = await api<AccessibilityProfileResponse>("/api/accessibility/profile", {
        method: "POST",
        body: JSON.stringify(draft),
      });
      setProfile(payload);
      setDraft(toDraft(payload));
      onStatusMessage?.(copy.saved, "success");
    } catch (saveError) {
      const text = String(saveError);
      setError(text);
      onStatusMessage?.(text, "error");
    } finally {
      setIsSaving(false);
    }
  };

  const resetDraft = () => {
    if (!profile) {
      return;
    }

    setDraft(toDraft(profile));
    setError("");
  };

  const sourceLabel =
    profile?.metadata.assignmentSource === "teacher"
      ? copy.sourceTeacher
      : profile?.metadata.assignmentSource === "preset"
        ? copy.sourcePreset
        : copy.sourceLocal;

  return (
    <Card className={cn("border-border/80 bg-card/90", disabled && "opacity-80")}>
      <CardHeader className="pb-3">
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div>
            <CardTitle>{copy.title}</CardTitle>
            <CardDescription className="mt-1 max-w-3xl">{copy.description}</CardDescription>
          </div>
          <div className="flex flex-wrap gap-2">
            <Badge variant="secondary">{sourceLabel}</Badge>
            {isDirty ? <Badge variant="warning">{copy.unsaved}</Badge> : null}
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <section className="space-y-2">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{copy.presets}</p>
            <p className="text-xs text-muted-foreground">{copy.localCustom}</p>
          </div>
          <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-6">
            {presetOrder.map((presetId) => (
              <Button
                key={presetId}
                type="button"
                variant={draft.activePreset === presetId ? "default" : "outline"}
                className="justify-start"
                onClick={() => void applyPreset(presetId)}
                disabled={disabled || isSaving}
              >
                {presetLabels[lang][presetId]}
              </Button>
            ))}
          </div>
        </section>

        <div className="grid gap-4 xl:grid-cols-[1.1fr_1fr]">
          <section className="rounded-xl border border-border/80 bg-background/55 p-3">
            <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{copy.interface}</p>

            <div className="mt-3 space-y-3">
              <div className="rounded-lg border border-border/70 bg-card/70 p-3">
                <div className="flex items-center justify-between gap-3">
                  <label htmlFor="a11y-scale" className="text-sm font-medium">
                    {copy.scale}
                  </label>
                  <span className="text-sm text-muted-foreground">{draft.ui.scalePercent}%</span>
                </div>
                <input
                  id="a11y-scale"
                  type="range"
                  min={100}
                  max={300}
                  step={5}
                  className="mt-2 w-full"
                  value={draft.ui.scalePercent}
                  onChange={(event) => setUi("scalePercent", Number(event.target.value))}
                  disabled={disabled || isSaving}
                />
              </div>

              <div className="grid gap-3 sm:grid-cols-2">
                <label className="space-y-1 text-sm">
                  <span className="font-medium">{copy.contrast}</span>
                  <select
                    className="h-10 w-full rounded-md border border-input bg-background px-3 text-sm"
                    value={draft.ui.contrastMode}
                    onChange={(event) => setUi("contrastMode", event.target.value as AccessibilityContrastMode)}
                    disabled={disabled || isSaving}
                  >
                    <option value="standard">{copy.standardContrast}</option>
                    <option value="aa">{copy.contrastAa}</option>
                    <option value="aaa">{copy.contrastAaa}</option>
                  </select>
                </label>

                <label className="space-y-1 text-sm">
                  <span className="font-medium">{copy.colorMode}</span>
                  <select
                    className="h-10 w-full rounded-md border border-input bg-background px-3 text-sm"
                    value={draft.ui.colorBlindMode}
                    onChange={(event) => setUi("colorBlindMode", event.target.value as AccessibilityColorBlindMode)}
                    disabled={disabled || isSaving}
                  >
                    <option value="none">{copy.colorNone}</option>
                    <option value="protanopia">{copy.protanopia}</option>
                    <option value="deuteranopia">{copy.deuteranopia}</option>
                    <option value="tritanopia">{copy.tritanopia}</option>
                  </select>
                </label>
              </div>

              <div className="grid gap-2">
                <SwitchRow
                  label={copy.invert}
                  checked={draft.ui.invertColors}
                  disabled={disabled || isSaving}
                  onChange={(next) => setUi("invertColors", next)}
                />
                <SwitchRow
                  label={copy.dyslexiaFont}
                  checked={draft.ui.dyslexiaFontEnabled}
                  disabled={disabled || isSaving}
                  onChange={(next) => setUi("dyslexiaFontEnabled", next)}
                />
                <SwitchRow
                  label={copy.largeCursor}
                  checked={draft.ui.largeCursorEnabled}
                  disabled={disabled || isSaving}
                  onChange={(next) => setUi("largeCursorEnabled", next)}
                />
                <SwitchRow
                  label={copy.focusHighlight}
                  checked={draft.ui.highlightFocusEnabled}
                  disabled={disabled || isSaving}
                  onChange={(next) => setUi("highlightFocusEnabled", next)}
                />
              </div>
            </div>
          </section>

          <section className="rounded-xl border border-border/80 bg-background/55 p-3">
            <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{copy.features}</p>
            <div className="mt-3 grid gap-2">
              <SwitchRow
                label={copy.visualAlerts}
                checked={draft.features.visualAlertsEnabled}
                disabled={disabled || isSaving}
                onChange={(next) => setFeature("visualAlertsEnabled", next)}
              />
              <SwitchRow
                label={copy.largeButtons}
                checked={draft.features.largeActionButtonsEnabled}
                disabled={disabled || isSaving}
                onChange={(next) => setFeature("largeActionButtonsEnabled", next)}
              />
              <SwitchRow
                label={copy.simpleNav}
                checked={draft.features.simplifiedNavigationEnabled}
                disabled={disabled || isSaving}
                onChange={(next) => setFeature("simplifiedNavigationEnabled", next)}
              />
              <SwitchRow
                label={copy.singleKey}
                checked={draft.features.singleKeyModeEnabled}
                disabled={disabled || isSaving}
                onChange={(next) => setFeature("singleKeyModeEnabled", next)}
              />
              <SwitchRow
                label={copy.tts}
                checked={draft.features.ttsTeacherMessagesEnabled}
                disabled={disabled || isSaving}
                onChange={(next) => setFeature("ttsTeacherMessagesEnabled", next)}
              />
              <SwitchRow
                label={copy.audioLesson}
                checked={draft.features.audioLessonModeEnabled}
                disabled={disabled || isSaving}
                onChange={(next) => setFeature("audioLessonModeEnabled", next)}
              />
              <SwitchRow
                label={copy.liveCaptions}
                checked={draft.features.liveCaptionsEnabled}
                disabled={disabled || isSaving}
                onChange={(next) => setFeature("liveCaptionsEnabled", next)}
              />
              <SwitchRow
                label={copy.voiceCommands}
                checked={draft.features.voiceCommandsEnabled}
                disabled={disabled || isSaving}
                onChange={(next) => setFeature("voiceCommandsEnabled", next)}
              />
            </div>
          </section>
        </div>

        <div className="rounded-lg border border-border/80 bg-background/55 p-3">
          <SwitchRow
            label={copy.teacherOverride}
            checked={draft.allowTeacherOverride}
            disabled={disabled || isSaving}
            onChange={setAllowTeacherOverride}
          />
          {profile?.metadata.assignedBy ? (
            <p className="mt-2 text-xs text-muted-foreground">
              {copy.assignedBy}: <span className="font-medium text-foreground">{profile.metadata.assignedBy}</span>
            </p>
          ) : null}
          {profile?.metadata.updatedAtUtc ? (
            <p className="mt-1 text-xs text-muted-foreground">
              {copy.lastUpdate}: {isoToLocal(profile.metadata.updatedAtUtc)}
            </p>
          ) : null}
        </div>

        {error ? (
          <div className="rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive">{error}</div>
        ) : null}

        <div className="flex flex-wrap items-center justify-end gap-2">
          <Button type="button" variant="ghost" onClick={resetDraft} disabled={!isDirty || disabled || isSaving}>
            {copy.reset}
          </Button>
          <Button type="button" onClick={() => void save()} disabled={!isDirty || disabled || isSaving}>
            {isSaving ? copy.saving : copy.save}
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

type SwitchRowProps = {
  label: string;
  checked: boolean;
  disabled?: boolean;
  onChange: (checked: boolean) => void;
};

function SwitchRow({ label, checked, disabled, onChange }: SwitchRowProps) {
  return (
    <label className="flex items-center justify-between gap-3 rounded-lg border border-border/70 bg-card/70 px-3 py-2.5 text-sm">
      <span className="leading-5">{label}</span>
      <input
        type="checkbox"
        className="h-4 w-4 rounded border-border accent-primary"
        checked={checked}
        onChange={(event) => onChange(event.target.checked)}
        disabled={disabled}
      />
    </label>
  );
}
