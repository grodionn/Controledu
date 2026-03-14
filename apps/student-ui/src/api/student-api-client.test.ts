import { StudentApiClient } from "./student-api-client";
import { afterEach, describe, expect, it, vi } from "vitest";

describe("StudentApiClient", () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    vi.restoreAllMocks();
    globalThis.fetch = originalFetch;
  });

  it("sends local token in X-Controledu-LocalToken header", async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(JSON.stringify({ status: "ok" }), {
        status: 200,
        headers: { "content-type": "application/json" },
      }),
    );

    globalThis.fetch = fetchMock;
    const api = new StudentApiClient(() => "local-token-xyz");

    await api.requestJson("/api/status", { method: "GET" });

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = new Headers(init.headers);
    expect(headers.get("X-Controledu-LocalToken")).toBe("local-token-xyz");
  });
});
