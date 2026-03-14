import commonEn from "./locales/common.en.json";
import commonKz from "./locales/common.kz.json";
import commonRu from "./locales/common.ru.json";
import studentEn from "./locales/student.en.json";
import studentKz from "./locales/student.kz.json";
import studentRu from "./locales/student.ru.json";

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

export const studentDictionary: Record<UiLanguage, LanguageDictionary> = {
  ru: mergeDictionaries(commonRu, studentRu),
  en: mergeDictionaries(commonEn, studentEn),
  kz: mergeDictionaries(commonKz, studentKz),
};
