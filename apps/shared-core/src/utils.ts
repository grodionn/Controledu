export function cn(...values: Array<string | false | null | undefined>): string {
  return values.filter((value): value is string => typeof value === "string" && value.length > 0).join(" ");
}

export function toDataUrl(framePayload: string | number[]): string {
  if (typeof framePayload === "string") {
    return `data:image/jpeg;base64,${framePayload}`;
  }

  let binary = "";
  for (const byte of framePayload) {
    binary += String.fromCharCode(byte);
  }

  return `data:image/jpeg;base64,${btoa(binary)}`;
}

export async function sha256Hex(bytes: Uint8Array): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", bytes as unknown as BufferSource);
  const hashBytes = Array.from(new Uint8Array(digest));
  return hashBytes.map((x) => x.toString(16).padStart(2, "0")).join("").toUpperCase();
}
