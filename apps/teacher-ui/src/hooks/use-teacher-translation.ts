import { useMemo } from "react";
import { teacherDictionary, type UiLanguage } from "../i18n";

type TranslateFn = (key: string) => string;

function localizeDetectionClass(t: TranslateFn, value: string): string {
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
}

function localizeStageSource(t: TranslateFn, value: string): string {
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
}

export function useTeacherTranslation(lang: UiLanguage) {
  return useMemo(() => {
    const t = (key: string) => teacherDictionary[lang][key] ?? key;
    return {
      t,
      localizeDetectionClass: (value: string) => localizeDetectionClass(t, value),
      localizeStageSource: (value: string) => localizeStageSource(t, value),
    };
  }, [lang]);
}
