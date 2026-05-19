import commonEn from "./locales/common.en.json";
import commonKz from "./locales/common.kz.json";
import commonRu from "./locales/common.ru.json";
import teacherEn from "./locales/teacher.en.json";
import teacherKz from "./locales/teacher.kz.json";
import teacherRu from "./locales/teacher.ru.json";
import { interpolate, mergeDictionaries, type LanguageDictionary, type UiLanguage } from "@controledu/shared-core/i18n";
export { interpolate };
export type { UiLanguage };

export const sharedI18nKeys = [
  "connected",
  "detectionProductionLocked",
  "detectionStorageHint",
  "disabled",
  "disconnected",
  "enabled",
  "pairingPin",
] as const;

export const teacherDictionary: Record<UiLanguage, LanguageDictionary> = {
  ru: mergeDictionaries(commonRu, teacherRu),
  en: mergeDictionaries(commonEn, teacherEn),
  kz: mergeDictionaries(commonKz, teacherKz),
};
