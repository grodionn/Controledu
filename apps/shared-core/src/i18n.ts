export type UiLanguage = "ru" | "en" | "kz";

export type LanguageDictionary = Record<string, string>;

export function mergeDictionaries(common: LanguageDictionary, specific: LanguageDictionary): LanguageDictionary {
  return { ...common, ...specific };
}

export function interpolate(template: string, values: Record<string, string | number>): string {
  let result = template;
  for (const [key, value] of Object.entries(values)) {
    result = result.replaceAll(`{${key}}`, String(value));
  }

  return result;
}
