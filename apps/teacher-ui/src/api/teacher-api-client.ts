import type {
  AccessibilityProfileUpdateDto,
  AlertItem,
  AuditItem,
  DetectionPolicy,
  PairPinResponse,
  StudentChatHistoryResponse,
  TeacherStudentChatMessage,
  UploadInitResponse,
} from "../lib/types";
import type { TeacherLiveCaptionRequestDto, TeacherSessionResponse, TeacherSttTranscribeResponse } from "../features/app/types";
import { requestJson, requestRaw, type RequestOptions } from "./http-client";

const TEACHER_TOKEN_HEADER = "X-Controledu-TeacherToken";

export class TeacherApiClient {
  private token = "";

  public setToken(token: string): void {
    this.token = token.trim();
  }

  public clearToken(): void {
    this.token = "";
  }

  public getToken(): string {
    return this.token;
  }

  private withAuth(init?: RequestInit): RequestInit {
    const headers = new Headers(init?.headers);
    if (this.token.length > 0) {
      headers.set(TEACHER_TOKEN_HEADER, this.token);
    }

    return { ...init, headers };
  }

  public async bootstrapSessionToken(): Promise<string> {
    const session = await requestJson<TeacherSessionResponse>("/api/session", { method: "GET" }, { retry: { maxAttempts: 3 } });
    const token = session.token?.trim() ?? "";
    if (!token) {
      throw new Error("Teacher API session token is missing.");
    }

    this.setToken(token);
    return token;
  }

  public async requestJson<T>(url: string, init?: RequestInit, options?: RequestOptions): Promise<T> {
    return requestJson<T>(url, this.withAuth(init), options);
  }

  public async requestRaw(url: string, init?: RequestInit, options?: RequestOptions): Promise<Response> {
    return requestRaw(url, this.withAuth(init), options);
  }

  public async postDesktopNotification(title: string, message: string, kind: "ai" | "signal" | "chat"): Promise<void> {
    await this.requestRaw("/api/desktop/notify", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ title, message, kind }),
    });
  }

  public async sendLiveCaption(clientId: string, payload: TeacherLiveCaptionRequestDto): Promise<{ ok: boolean; message?: string }> {
    return this.requestJson<{ ok: boolean; message?: string }>(`/api/students/${encodeURIComponent(clientId)}/live-caption`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });
  }

  public async transcribeMicrophoneChunk(formData: FormData): Promise<TeacherSttTranscribeResponse> {
    return this.requestJson<TeacherSttTranscribeResponse>("/api/speech/stt/transcribe", {
      method: "POST",
      body: formData,
    });
  }

  public async getAuditLatest(take = 120): Promise<AuditItem[]> {
    return this.requestJson<AuditItem[]>(`/api/audit/latest?take=${Math.max(1, take)}`, { method: "GET" }, { retry: { maxAttempts: 3 } });
  }

  public async getDetectionEvents(take = 200): Promise<AlertItem[]> {
    return this.requestJson<AlertItem[]>(`/api/detection/events?take=${Math.max(1, take)}`, { method: "GET" }, { retry: { maxAttempts: 3 } });
  }

  public async getDetectionPolicy(): Promise<DetectionPolicy> {
    return this.requestJson<DetectionPolicy>("/api/detection/settings", { method: "GET" }, { retry: { maxAttempts: 3 } });
  }

  public async getStudentChatHistory(clientId: string, take = 120): Promise<StudentChatHistoryResponse> {
    return this.requestJson<StudentChatHistoryResponse>(`/api/students/${encodeURIComponent(clientId)}/chat?take=${Math.max(1, take)}`, {
      method: "GET",
    });
  }

  public async generatePairingPin(): Promise<PairPinResponse> {
    return this.requestJson<PairPinResponse>("/api/pairing/pin", { method: "POST" });
  }

  public async initFileUpload(request: {
    fileName: string;
    fileSize: number;
    sha256: string;
    chunkSize: number;
    uploadedBy: string;
  }): Promise<UploadInitResponse> {
    return this.requestJson<UploadInitResponse>("/api/files/upload/init", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    });
  }

  public async uploadFileChunk(transferId: string, chunkIndex: number, chunk: Uint8Array, chunkSha256: string): Promise<void> {
    const safeChunk = new Uint8Array(chunk.byteLength);
    safeChunk.set(chunk);

    await this.requestRaw(`/api/files/upload/${transferId}/chunk/${chunkIndex}`, {
      method: "PUT",
      headers: {
        "Content-Type": "application/octet-stream",
        "X-Chunk-Sha256": chunkSha256,
      },
      body: safeChunk.buffer,
    }, {
      retry: {
        maxAttempts: 3,
        allowUnsafeMethods: true,
      },
    });
  }

  public async dispatchFileTransfer(transferId: string, targetClientIds: string[]): Promise<void> {
    await this.requestRaw(`/api/files/${transferId}/dispatch`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ transferId, targetClientIds }),
    });
  }

  public async removeStudent(clientId: string): Promise<void> {
    await this.requestRaw(`/api/students/${encodeURIComponent(clientId)}`, { method: "DELETE" });
  }

  public async assignAccessibilityProfile(clientId: string, profile: AccessibilityProfileUpdateDto): Promise<{ ok: boolean; message?: string }> {
    return this.requestJson<{ ok: boolean; message?: string }>(`/api/students/${encodeURIComponent(clientId)}/accessibility-profile`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        teacherDisplayName: "Teacher Console",
        profile,
      }),
    });
  }

  public async sendTeacherTts(clientId: string, payload: {
    messageText: string;
    languageCode?: string;
    voiceName?: string | null;
    speakingRate?: number;
    pitch?: number;
    selfHostBaseUrl?: string;
    selfHostApiToken?: string;
    selfHostTtsPath: string;
  }): Promise<{ ok: boolean; message?: string }> {
    return this.requestJson<{ ok: boolean; message?: string }>(`/api/students/${encodeURIComponent(clientId)}/tts`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        teacherDisplayName: "Teacher Console",
        ...payload,
      }),
    });
  }

  public async sendTeacherChat(clientId: string, text: string): Promise<{ ok: boolean; message?: string; chat?: TeacherStudentChatMessage }> {
    return this.requestJson<{ ok: boolean; message?: string; chat?: TeacherStudentChatMessage }>(`/api/students/${encodeURIComponent(clientId)}/chat`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        teacherDisplayName: "Teacher Console",
        text,
      }),
    });
  }
}

export const teacherApiClient = new TeacherApiClient();
