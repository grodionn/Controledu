import type {
  AccessibilityProfileResponse,
  AccessibilityProfileUpdateRequest,
  DetectionStatusResponse,
  DiagnosticsExportResponse,
  DiscoveredServer,
  StatusResponse,
} from "../types";
import { requestJson, type RequestOptions } from "./http-client";

const LOCAL_TOKEN_HEADER = "X-Controledu-LocalToken";

type SessionResponse = {
  token: string;
};

type OkResponse = {
  ok: boolean;
  message?: string;
};

function withJsonBody(body: unknown): { headers: HeadersInit; body: string } {
  return {
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  };
}

export class StudentApiClient {
  constructor(private readonly getToken: () => string) {}

  private withAuth(init?: RequestInit): RequestInit {
    const token = this.getToken().trim();
    const headers = new Headers(init?.headers);
    if (token.length > 0) {
      headers.set(LOCAL_TOKEN_HEADER, token);
    }

    return { ...init, headers };
  }

  public async requestJson<T>(url: string, init?: RequestInit, options?: RequestOptions): Promise<T> {
    return requestJson<T>(url, this.withAuth(init), options);
  }

  public async createSession(): Promise<SessionResponse> {
    return requestJson<SessionResponse>("/api/session", { method: "GET" }, { retry: { maxAttempts: 3 } });
  }

  public async getStatus(): Promise<StatusResponse> {
    return this.requestJson<StatusResponse>("/api/status", { method: "GET" }, { retry: { maxAttempts: 3 } });
  }

  public async getDetectionStatus(): Promise<DetectionStatusResponse> {
    return this.requestJson<DetectionStatusResponse>("/api/detection/status", { method: "GET" }, { retry: { maxAttempts: 3 } });
  }

  public async discoverServers(): Promise<DiscoveredServer[]> {
    const json = withJsonBody({});
    return this.requestJson<DiscoveredServer[]>("/api/discovery", { method: "POST", ...json });
  }

  public async setupAdminPassword(request: {
    password: string;
    confirmPassword: string;
    enableAgentAutoStart: boolean;
  }): Promise<OkResponse> {
    const json = withJsonBody(request);
    return this.requestJson<OkResponse>("/api/setup/admin-password", { method: "POST", ...json });
  }

  public async pair(request: { pin: string; serverAddress: string }): Promise<OkResponse> {
    const json = withJsonBody(request);
    return this.requestJson<OkResponse>("/api/pairing", { method: "POST", ...json });
  }

  public async unpair(adminPassword: string): Promise<OkResponse> {
    const json = withJsonBody({ adminPassword });
    return this.requestJson<OkResponse>("/api/unpair", { method: "POST", ...json });
  }

  public async saveAgentAutoStartPolicy(enabled: boolean, adminPassword?: string): Promise<OkResponse> {
    const json = withJsonBody({
      enabled,
      adminPassword: enabled ? undefined : adminPassword,
    });
    return this.requestJson<OkResponse>("/api/agent/autostart", { method: "POST", ...json });
  }

  public async startAgent(): Promise<OkResponse> {
    const json = withJsonBody({});
    return this.requestJson<OkResponse>("/api/agent/start", { method: "POST", ...json });
  }

  public async stopAgent(adminPassword: string): Promise<OkResponse> {
    const json = withJsonBody({ adminPassword });
    return this.requestJson<OkResponse>("/api/agent/stop", { method: "POST", ...json });
  }

  public async verifyAdminPassword(adminPassword: string): Promise<OkResponse> {
    const json = withJsonBody({ adminPassword });
    return this.requestJson<OkResponse>("/api/admin/verify", { method: "POST", ...json });
  }

  public async updateDeviceName(deviceName: string, adminPassword: string): Promise<OkResponse> {
    const json = withJsonBody({ deviceName, adminPassword });
    return this.requestJson<OkResponse>("/api/device-name", { method: "POST", ...json });
  }

  public async shutdownSystem(adminPassword: string, stopAgent: boolean): Promise<OkResponse> {
    const json = withJsonBody({ adminPassword, stopAgent });
    return this.requestJson<OkResponse>("/api/system/shutdown", { method: "POST", ...json });
  }

  public async runDetectionSelfTest(adminPassword: string): Promise<OkResponse> {
    const json = withJsonBody({ adminPassword });
    return this.requestJson<OkResponse>("/api/detection/self-test", { method: "POST", ...json });
  }

  public async exportDetectionDiagnostics(adminPassword: string): Promise<DiagnosticsExportResponse> {
    const json = withJsonBody({ adminPassword });
    return this.requestJson<DiagnosticsExportResponse>("/api/detection/export-diagnostics", { method: "POST", ...json });
  }

  public async getAccessibilityProfile(): Promise<AccessibilityProfileResponse> {
    return this.requestJson<AccessibilityProfileResponse>("/api/accessibility/profile", { method: "GET" }, { retry: { maxAttempts: 3 } });
  }

  public async updateAccessibilityProfile(request: AccessibilityProfileUpdateRequest): Promise<AccessibilityProfileResponse> {
    const json = withJsonBody(request);
    return this.requestJson<AccessibilityProfileResponse>("/api/accessibility/profile", { method: "POST", ...json });
  }

  public async applyAccessibilityPreset(presetId: string): Promise<AccessibilityProfileResponse> {
    const json = withJsonBody({ presetId });
    return this.requestJson<AccessibilityProfileResponse>("/api/accessibility/profile/preset", { method: "POST", ...json });
  }
}

export function createStudentApiClient(getToken: () => string): StudentApiClient {
  return new StudentApiClient(getToken);
}
