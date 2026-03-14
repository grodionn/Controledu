import { useCallback } from "react";
import { studentDictionary, type UiLanguage } from "../i18n";

export function useStudentTranslation(lang: UiLanguage) {
  const t = useCallback((key: string) => studentDictionary[lang][key] ?? key, [lang]);

  const tf = useCallback(
    (key: string, values: Record<string, string | number>) => {
      let text = t(key);
      for (const [name, value] of Object.entries(values)) {
        text = text.replaceAll(`{${name}}`, String(value));
      }
      return text;
    },
    [t],
  );

  return { t, tf };
}
