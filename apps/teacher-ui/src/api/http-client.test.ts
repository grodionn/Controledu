import { ApiError, requestJson } from "./http-client";
import { afterEach, describe, expect, it, vi } from "vitest";

describe("teacher http-client", () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    vi.restoreAllMocks();
    globalThis.fetch = originalFetch;
  });

  it("retries GET request on retryable status", async () => {
    const fetchMock = vi
      .fn<typeof fetch>()
      .mockResolvedValueOnce(
        new Response("retry", {
          status: 503,
          statusText: "Service Unavailable",
          headers: { "content-type": "text/plain" },
        }),
      )
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ ok: true }), {
          status: 200,
          headers: { "content-type": "application/json" },
        }),
      );

    globalThis.fetch = fetchMock;

    const result = await requestJson<{ ok: boolean }>(
      "/api/test",
      { method: "GET" },
      { retry: { maxAttempts: 2, baseDelayMs: 1, maxDelayMs: 1 } },
    );

    expect(result.ok).toBe(true);
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("does not retry POST request by default", async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response("failed", {
        status: 503,
        statusText: "Service Unavailable",
        headers: { "content-type": "text/plain" },
      }),
    );

    globalThis.fetch = fetchMock;

    await expect(
      requestJson("/api/test", { method: "POST" }, { retry: { maxAttempts: 3 } }),
    ).rejects.toBeInstanceOf(ApiError);

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("fails when JSON endpoint returns non-json payload", async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response("plain", {
        status: 200,
        headers: { "content-type": "text/plain" },
      }),
    );

    globalThis.fetch = fetchMock;

    await expect(requestJson("/api/test", { method: "GET" })).rejects.toThrow("Expected JSON response");
  });
});
