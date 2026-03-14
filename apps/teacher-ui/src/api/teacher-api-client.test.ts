import { TeacherApiClient } from "./teacher-api-client";
import { afterEach, describe, expect, it, vi } from "vitest";

describe("TeacherApiClient", () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    vi.restoreAllMocks();
    globalThis.fetch = originalFetch;
  });

  it("stores bootstrap token and sends it in auth header", async () => {
    const fetchMock = vi
      .fn<typeof fetch>()
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ token: "teacher-token-123" }), {
          status: 200,
          headers: { "content-type": "application/json" },
        }),
      )
      .mockResolvedValueOnce(
        new Response("", {
          status: 200,
          headers: { "content-type": "application/json" },
        }),
      );

    globalThis.fetch = fetchMock;
    const api = new TeacherApiClient();

    const token = await api.bootstrapSessionToken();
    expect(token).toBe("teacher-token-123");

    await api.requestRaw("/api/audit/latest", { method: "GET" });

    const secondCall = fetchMock.mock.calls[1];
    const init = secondCall[1] as RequestInit;
    const headers = new Headers(init.headers);
    expect(headers.get("X-Controledu-TeacherToken")).toBe("teacher-token-123");
  });
});
