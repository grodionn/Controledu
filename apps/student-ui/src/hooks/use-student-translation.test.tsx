import { renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { useStudentTranslation } from "./use-student-translation";

describe("useStudentTranslation", () => {
  it("returns localized text and interpolates placeholders", () => {
    const { result } = renderHook(() => useStudentTranslation("en"));

    expect(result.current.t("monitoringActive")).toBe("Monitoring Active");
    expect(result.current.tf("onboardingAutoSelectNotice", { count: 3 })).toContain("3");
  });

  it("falls back to key when translation is missing", () => {
    const { result } = renderHook(() => useStudentTranslation("en"));

    expect(result.current.t("missing-i18n-key")).toBe("missing-i18n-key");
  });
});
