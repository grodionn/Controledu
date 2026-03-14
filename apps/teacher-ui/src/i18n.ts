import commonEn from "./locales/common.en.json";
import commonKz from "./locales/common.kz.json";
import commonRu from "./locales/common.ru.json";
import teacherEn from "./locales/teacher.en.json";
import teacherKz from "./locales/teacher.kz.json";
import teacherRu from "./locales/teacher.ru.json";

export type UiLanguage = "ru" | "en" | "kz";

type LanguageDictionary = Record<string, string>;

export const sharedI18nKeys = [
  "connected",
  "detectionProductionLocked",
  "detectionStorageHint",
  "disabled",
  "disconnected",
  "enabled",
  "pairingPin",
] as const;

function mergeDictionaries(common: LanguageDictionary, specific: LanguageDictionary): LanguageDictionary {
  return { ...common, ...specific };
}

export const teacherDictionary: Record<UiLanguage, LanguageDictionary> = {
  ru: mergeDictionaries(commonRu, teacherRu),
  en: mergeDictionaries(commonEn, teacherEn),
  kz: mergeDictionaries(commonKz, teacherKz),
};

export function interpolate(template: string, values: Record<string, string>): string {
  let result = template;
  for (const [key, value] of Object.entries(values)) {
    result = result.replaceAll(`{${key}}`, value);
  }

  return result;
}
