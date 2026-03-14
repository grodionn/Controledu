import { renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { useTeacherTranslation } from "./use-teacher-translation";

describe("useTeacherTranslation", () => {
  it("localizes known detection class and stage source", () => {
    const { result } = renderHook(() => useTeacherTranslation("en"));

    expect(result.current.localizeDetectionClass("ChatGpt")).toBe("ChatGPT");
    expect(result.current.localizeStageSource("MetadataRule")).toBe("Metadata");
  });

  it("falls back to key/value for unknown entries", () => {
    const { result } = renderHook(() => useTeacherTranslation("en"));

    expect(result.current.t("missing-i18n-key")).toBe("missing-i18n-key");
    expect(result.current.localizeDetectionClass("CustomDetectorClass")).toBe("CustomDetectorClass");
  });
});
