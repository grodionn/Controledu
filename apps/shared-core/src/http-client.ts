export class ApiError extends Error {
  public readonly method: string;
  public readonly url: string;
  public readonly status: number;
  public readonly statusText: string;
  public readonly bodyText: string;

  constructor(message: string, details: { method: string; url: string; status: number; statusText: string; bodyText: string }) {
    super(message);
    this.name = "ApiError";
    this.method = details.method;
    this.url = details.url;
    this.status = details.status;
    this.statusText = details.statusText;
    this.bodyText = details.bodyText;
  }
}

export type RetryOptions = {
  maxAttempts?: number;
  baseDelayMs?: number;
  maxDelayMs?: number;
  allowUnsafeMethods?: boolean;
  retryOnStatuses?: number[];
};

export type RequestOptions = {
  retry?: RetryOptions;
};

const IDEMPOTENT_METHODS = new Set(["GET", "HEAD", "OPTIONS"]);
const DEFAULT_RETRY_STATUSES = new Set([408, 425, 429, 500, 502, 503, 504]);

function sleep(delayMs: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, delayMs));
}

function isNetworkError(error: unknown): boolean {
  return error instanceof TypeError;
}

function resolveMethod(init?: RequestInit): string {
  return (init?.method ?? "GET").toUpperCase();
}

function shouldRetry(method: string, attempt: number, maxAttempts: number, error: unknown, status?: number, retry?: RetryOptions): boolean {
  if (attempt >= maxAttempts) {
    return false;
  }

  const allowUnsafeMethods = retry?.allowUnsafeMethods ?? false;
  if (!allowUnsafeMethods && !IDEMPOTENT_METHODS.has(method)) {
    return false;
  }

  if (typeof status === "number") {
    const retryStatuses = retry?.retryOnStatuses?.length
      ? new Set(retry.retryOnStatuses)
      : DEFAULT_RETRY_STATUSES;
    return retryStatuses.has(status);
  }

  return isNetworkError(error);
}

function resolveBackoffDelay(attempt: number, retry?: RetryOptions): number {
  const base = Math.max(50, retry?.baseDelayMs ?? 200);
  const cap = Math.max(base, retry?.maxDelayMs ?? 2000);
  const exponential = Math.min(cap, base * 2 ** (attempt - 1));
  const jitter = Math.floor(Math.random() * 60);
  return exponential + jitter;
}

async function readResponseText(response: Response): Promise<string> {
  try {
    return await response.text();
  } catch {
    return "";
  }
}

function buildApiError(method: string, url: string, response: Response, bodyText: string): ApiError {
  const fallback = `${method} ${url} failed (${response.status} ${response.statusText}).`;
  return new ApiError(bodyText || fallback, {
    method,
    url,
    status: response.status,
    statusText: response.statusText,
    bodyText,
  });
}

export async function requestRaw(url: string, init?: RequestInit, options?: RequestOptions): Promise<Response> {
  const method = resolveMethod(init);
  const retry = options?.retry;
  const maxAttempts = Math.max(1, retry?.maxAttempts ?? 2);
  let attempt = 1;
  let lastError: unknown;

  while (attempt <= maxAttempts) {
    try {
      const response = await fetch(url, init);
      if (response.ok) {
        return response;
      }

      const bodyText = await readResponseText(response);
      const apiError = buildApiError(method, url, response, bodyText);
      if (!shouldRetry(method, attempt, maxAttempts, apiError, response.status, retry)) {
        throw apiError;
      }

      await sleep(resolveBackoffDelay(attempt, retry));
      attempt += 1;
      continue;
    } catch (error) {
      lastError = error;
      if (!shouldRetry(method, attempt, maxAttempts, error, undefined, retry)) {
        throw error;
      }

      await sleep(resolveBackoffDelay(attempt, retry));
      attempt += 1;
    }
  }

  throw lastError instanceof Error ? lastError : new Error("Request failed.");
}

export async function requestJson<T>(url: string, init?: RequestInit, options?: RequestOptions): Promise<T> {
  const response = await requestRaw(url, init, options);
  const bodyText = await readResponseText(response);

  if (!bodyText.trim()) {
    return undefined as T;
  }

  const contentType = response.headers.get("content-type") ?? "";
  if (!contentType.includes("application/json")) {
    throw new Error(`Expected JSON response from ${url}, got '${contentType || "unknown"}'.`);
  }

  return JSON.parse(bodyText) as T;
}
