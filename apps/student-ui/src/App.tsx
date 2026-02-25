import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { DetectionStatusResponse, DiagnosticsExportResponse, DiscoveredServer, StatusResponse } from "./types";
import { studentDictionary, UiLanguage } from "./i18n";
import { AccessibilitySettingsPanel } from "./components/accessibility-settings-panel";
import { ThemeToggle } from "./components/theme-toggle";
import { Badge } from "./components/ui/badge";
import { Button } from "./components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "./components/ui/card";
import { Input } from "./components/ui/input";
import { cn } from "./lib/utils";

const HEADER = "X-Controledu-LocalToken";
const THEME_KEY = "controledu.student.theme";
const LANG_KEY = "controledu.student.lang";
const AUTO_LOCK_TIMEOUT_MS = 5 * 60 * 1000;
const MAIN_CONTENT_ID = "student-main-content";
const LOCK_DIALOG_TITLE_ID = "student-lock-dialog-title";
const LOCK_DIALOG_DESC_ID = "student-lock-dialog-desc";

type Theme = "light" | "dark";
type MessageTone = "neutral" | "success" | "error";
type ApiFn = <T>(path: string, init?: RequestInit) => Promise<T>;

function App() {
  const [theme, setTheme] = useState<Theme>(() => (localStorage.getItem(THEME_KEY) === "light" ? "light" : "dark"));
  const [lang, setLang] = useState<UiLanguage>(() => {
    const value = localStorage.getItem(LANG_KEY);
    return value === "en" || value === "kz" ? value : "ru";
  });
  const [token, setToken] = useState<string>("");
  const [status, setStatus] = useState<StatusResponse | null>(null);
  const [message, setMessage] = useState<string>("Initializing secure local session...");
  const [messageTone, setMessageTone] = useState<MessageTone>("neutral");
  const [isUnlocked, setIsUnlocked] = useState(false);

  const [setupPassword, setSetupPassword] = useState("");
  const [setupConfirm, setSetupConfirm] = useState("");
  const [setupAutoStart, setSetupAutoStart] = useState(true);

  const [pin, setPin] = useState("");
  const [manualAddress, setManualAddress] = useState("");
  const [selectedServer, setSelectedServer] = useState("");
  const [servers, setServers] = useState<DiscoveredServer[]>([]);
  const [showManualAddress, setShowManualAddress] = useState(false);

  const [unpairPassword, setUnpairPassword] = useState("");
  const [autoStart, setAutoStart] = useState(false);
  const [autoStartPassword, setAutoStartPassword] = useState("");
  const [stopPassword, setStopPassword] = useState("");
  const [unlockPassword, setUnlockPassword] = useState("");
  const [deviceName, setDeviceName] = useState("");
  const [renamePassword, setRenamePassword] = useState("");
  const [shutdownPassword, setShutdownPassword] = useState("");
  const [detectionStatus, setDetectionStatus] = useState<DetectionStatusResponse | null>(null);
  const [detectionAdminPassword, setDetectionAdminPassword] = useState("");
  const [diagnosticsArchivePath, setDiagnosticsArchivePath] = useState("");
  const lastActivityAtRef = useRef(Date.now());

  const t = useCallback((key: string) => studentDictionary[lang][key] ?? key, [lang]);
  const setStatusMessage = (text: string, tone: MessageTone = "neutral") => {
    setMessage(text);
    setMessageTone(tone);
  };

  useEffect(() => {
    document.documentElement.classList.toggle("dark", theme === "dark");
    localStorage.setItem(THEME_KEY, theme);
  }, [theme]);

  useEffect(() => {
    localStorage.setItem(LANG_KEY, lang);
  }, [lang]);

  const api = useMemo<ApiFn>(() => {
    return async <T,>(path: string, init?: RequestInit): Promise<T> => {
      const response = await fetch(path, {
        ...init,
        headers: {
          ...(init?.headers ?? {}),
          [HEADER]: token,
          ...(init?.body ? { "Content-Type": "application/json" } : {}),
        },
      });

      const bodyText = await response.text();
      if (!response.ok) {
        throw new Error(bodyText || `Request failed (${response.status}).`);
      }

      if (!bodyText) {
        return undefined as T;
      }

      const contentType = response.headers.get("content-type") ?? "";
      if (!contentType.includes("application/json")) {
        throw new Error(`Unexpected non-JSON response for ${path}.`);
      }

      return JSON.parse(bodyText) as T;
    };
  }, [token]);

  const refreshStatus = useCallback(async () => {
    if (!token) {
      return;
    }

    const [next, detection] = await Promise.all([
      api<StatusResponse>("/api/status"),
      api<DetectionStatusResponse>("/api/detection/status"),
    ]);

    setStatus(next);
    setDetectionStatus(detection);
    setAutoStart(next.agentAutoStart);
    setDeviceName(next.deviceName ?? "");

    if (!next.hasAdminPassword) {
      setIsUnlocked(true);
    }
  }, [api, token]);

  useEffect(() => {
    let cancelled = false;

    (async () => {
      try {
        const response = await fetch("/api/session");
        if (!response.ok) {
          throw new Error(await response.text());
        }

        const payload = (await response.json()) as { token: string };
        if (!cancelled) {
          setToken(payload.token);
          setStatusMessage("Session established.", "success");
        }
      } catch (error) {
        if (!cancelled) {
          setStatusMessage(`Session error: ${String(error)}`, "error");
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!token) {
      return;
    }

    refreshStatus()
      .then(() => setStatusMessage("Ready", "success"))
      .catch((error) => setStatusMessage(`Status error: ${String(error)}`, "error"));

    const interval = window.setInterval(() => {
      refreshStatus().catch(() => undefined);
    }, 8000);

    return () => window.clearInterval(interval);
  }, [token, refreshStatus]);

  useEffect(() => {
    if (!status?.hasAdminPassword || !isUnlocked) {
      return;
    }

    const markActivity = () => {
      lastActivityAtRef.current = Date.now();
    };

    const timer = window.setInterval(() => {
      if (Date.now() - lastActivityAtRef.current < AUTO_LOCK_TIMEOUT_MS) {
        return;
      }

      setIsUnlocked(false);
      setUnlockPassword("");
      setStatusMessage("UI locked due to inactivity. Enter admin password.", "neutral");
    }, 10_000);

    window.addEventListener("pointerdown", markActivity, true);
    window.addEventListener("keydown", markActivity, true);

    return () => {
      window.clearInterval(timer);
      window.removeEventListener("pointerdown", markActivity, true);
      window.removeEventListener("keydown", markActivity, true);
    };
  }, [isUnlocked, status?.hasAdminPassword]);

  const setPassword = useMutation({
    mutationFn: async () => {
      await api<{ ok: boolean; message?: string }>("/api/setup/admin-password", {
        method: "POST",
        body: JSON.stringify({
          password: setupPassword,
          confirmPassword: setupConfirm,
          enableAgentAutoStart: setupAutoStart,
        }),
      });
    },
    onSuccess: async () => {
      setSetupPassword("");
      setSetupConfirm("");
      setIsUnlocked(true);
      lastActivityAtRef.current = Date.now();
      setStatusMessage("Admin password configured.", "success");
      await refreshStatus();
    },
    onError: (error) => setStatusMessage(String(error), "error"),
  });

  const discover = useMutation({
    mutationFn: async () => api<DiscoveredServer[]>("/api/discovery", { method: "POST", body: JSON.stringify({}) }),
    onSuccess: (list) => {
      setServers(list);
      if (list.length > 0) {
        const recommended = list.find((server) => server.isRecommended) ?? list[0];
        setSelectedServer(recommended.baseUrl);
        setManualAddress("");
        setStatusMessage(`Found ${list.length} server node(s). Recommended target selected automatically.`, "success");
        return;
      }

      setSelectedServer("");
      setStatusMessage("No servers found. Use manual address.", "success");
    },
    onError: (error) => setStatusMessage(String(error), "error"),
  });

  const pair = useMutation({
    mutationFn: async () => {
      const serverAddress = selectedServer || manualAddress;
      if (!pin.trim()) {
        throw new Error("Pairing PIN is required.");
      }
      if (!serverAddress.trim()) {
        throw new Error("Select discovered server or provide manual address.");
      }

      await api<{ ok: boolean; message?: string }>("/api/pairing", {
        method: "POST",
        body: JSON.stringify({ pin, serverAddress }),
      });
    },
    onSuccess: async () => {
      setPin("");
      setManualAddress("");
      setStatusMessage("Pairing completed.", "success");
      await refreshStatus();
    },
    onError: (error) => setStatusMessage(String(error), "error"),
  });

  const unpair = useMutation({
    mutationFn: async () => {
      await api<{ ok: boolean; message?: string }>("/api/unpair", {
        method: "POST",
        body: JSON.stringify({ adminPassword: unpairPassword }),
      });
    },
    onSuccess: async () => {
      setUnpairPassword("");
      setStatusMessage("Disconnected. Device can be connected to a new server.", "success");
      await refreshStatus();
    },
    onError: (error) => setStatusMessage(String(error), "error"),
  });

  const saveAutoStart = useMutation({
    mutationFn: async () => {
      if (!autoStart && !autoStartPassword.trim()) {
        throw new Error("Admin password is required to disable autostart.");
      }

      await api<{ ok: boolean; message?: string }>("/api/agent/autostart", {
        method: "POST",
        body: JSON.stringify({
          enabled: autoStart,
          adminPassword: autoStart ? undefined : autoStartPassword,
        }),
      });
    },
    onSuccess: async () => {
      if (!autoStart) {
        setAutoStartPassword("");
      }
      setStatusMessage("Agent autostart policy updated.", "success");
      await refreshStatus();
    },
    onError: (error) => setStatusMessage(String(error), "error"),
  });

  const startAgent = useMutation({
    mutationFn: async () => api<{ ok: boolean; message?: string }>("/api/agent/start", { method: "POST", body: JSON.stringify({}) }),
    onSuccess: async () => {
      setStatusMessage("Agent start requested.", "success");
      await refreshStatus();
    },
    onError: (error) => setStatusMessage(String(error), "error"),
  });

  const stopAgent = useMutation({
    mutationFn: async () => {
      if (!stopPassword.trim()) {
        throw new Error("Admin password is required to stop the agent.");
      }

      await api<{ ok: boolean; message?: string }>("/api/agent/stop", {
        method: "POST",
        body: JSON.stringify({ adminPassword: stopPassword }),
      });
    },
    onSuccess: async () => {
      setStopPassword("");
      setStatusMessage("Agent stop requested.", "success");
      await refreshStatus();
    },
    onError: (error) => setStatusMessage(String(error), "error"),
  });

  const unlockUi = useMutation({
    mutationFn: async () => {
      if (!unlockPassword.trim()) {
        throw new Error("Admin password is required.");
      }

      await api<{ ok: boolean; message?: string }>("/api/admin/verify", {
        method: "POST",
        body: JSON.stringify({ adminPassword: unlockPassword }),
      });
    },
    onSuccess: () => {
      setUnlockPassword("");
      setIsUnlocked(true);
      lastActivityAtRef.current = Date.now();
      setStatusMessage("UI unlocked.", "success");
    },
    onError: (error) => setStatusMessage(String(error), "error"),
  });

  const renameDevice = useMutation({
    mutationFn: async () => {
      if (!deviceName.trim()) {
        throw new Error("Device name is required.");
      }

      if (!renamePassword.trim()) {
        throw new Error("Admin password is required.");
      }

      await api<{ ok: boolean; message?: string }>("/api/device-name", {
        method: "POST",
        body: JSON.stringify({ deviceName: deviceName.trim(), adminPassword: renamePassword }),
      });
    },
    onSuccess: async () => {
      setRenamePassword("");
      setStatusMessage("Device name updated.", "success");
      await refreshStatus();
    },
    onError: (error) => setStatusMessage(String(error), "error"),
  });

  const shutdownProgram = useMutation({
    mutationFn: async () => {
      if (!shutdownPassword.trim()) {
        throw new Error("Admin password is required.");
      }

      await api<{ ok: boolean; message?: string }>("/api/system/shutdown", {
        method: "POST",
        body: JSON.stringify({ adminPassword: shutdownPassword, stopAgent: true }),
      });
    },
    onSuccess: () => {
      setShutdownPassword("");
      setStatusMessage("Shutdown requested.", "success");
    },
    onError: (error) => setStatusMessage(String(error), "error"),
  });

  const runDetectionSelfTest = useMutation({
    mutationFn: async () => {
      if (!detectionAdminPassword.trim()) {
        throw new Error("Admin password is required.");
      }

      await api<{ ok: boolean; message?: string }>("/api/detection/self-test", {
        method: "POST",
        body: JSON.stringify({ adminPassword: detectionAdminPassword }),
      });
    },
    onSuccess: () => setStatusMessage("Self-test alert has been queued.", "success"),
    onError: (error) => setStatusMessage(String(error), "error"),
  });

  const exportDetectionDiagnostics = useMutation({
    mutationFn: async () => {
      if (!detectionAdminPassword.trim()) {
        throw new Error("Admin password is required.");
      }

      return api<DiagnosticsExportResponse>("/api/detection/export-diagnostics", {
        method: "POST",
        body: JSON.stringify({ adminPassword: detectionAdminPassword }),
      });
    },
    onSuccess: (payload) => {
      setDiagnosticsArchivePath(payload.archivePath);
      setStatusMessage("Diagnostics exported.", "success");
    },
    onError: (error) => setStatusMessage(String(error), "error"),
  });

  const showSetup = status ? !status.hasAdminPassword : false;
  const showPairing = status ? status.hasAdminPassword && !status.isPaired : false;
  const showStatus = status ? status.hasAdminPassword && status.isPaired : false;
  const isLocked = Boolean(status?.hasAdminPassword) && !isUnlocked;
  const onboardingStep = showSetup ? 1 : showPairing ? 2 : 0;
  const setupPasswordMinLength = setupPassword.length >= 8;
  const setupPasswordsMatch = setupPassword.length > 0 && setupPassword === setupConfirm;
  const canSubmitSetup = setupPasswordMinLength && setupPasswordsMatch && !setPassword.isPending;
  const selectedEndpoint = (selectedServer || manualAddress).trim();
  const selectedServerInfo = useMemo(
    () => servers.find((server) => server.baseUrl === selectedServer) ?? null,
    [servers, selectedServer],
  );
  const canSubmitPair = pin.trim().length > 0 && selectedEndpoint.length > 0 && !pair.isPending;
  const statusMessageRole = messageTone === "error" ? "alert" : "status";
  const statusMessageLive = messageTone === "error" ? "assertive" : "polite";

  return (
    <div className="min-h-screen px-2.5 py-3 sm:px-4 sm:py-4 lg:px-5 lg:py-5">
      <a href={`#${MAIN_CONTENT_ID}`} className="skip-link">
        Skip to main content
      </a>
      <main
        id={MAIN_CONTENT_ID}
        aria-hidden={isLocked || undefined}
        className={cn("mx-auto max-w-[1140px] space-y-2.5 transition", isLocked && "pointer-events-none select-none blur-[1.8px]")}
      >
        <Card className="border-border/80 bg-panel-gradient/45 shadow-sm backdrop-blur-md">
          <CardContent className="flex flex-wrap items-center justify-between gap-2.5 pt-4">
            <div>
              <p className="text-xs uppercase tracking-[0.16em] text-muted-foreground">{t("studentConsole")}</p>
              <h1 className="text-lg font-semibold lg:text-xl">{t("deviceControl")}</h1>
              <p className="text-xs text-muted-foreground sm:text-sm">{t("securityHint")}</p>
            </div>
            <div className="flex flex-wrap items-center gap-1.5 sm:gap-2">
              <Badge variant={status?.monitoringActive ? "success" : "warning"} role="status" aria-live="polite" aria-atomic="true">
                {status?.monitoringActive ? t("monitoringActive") : t("notConnected")}
              </Badge>
              {status?.hasAdminPassword && isUnlocked && (
                <Button
                  variant="secondary"
                  onClick={() => {
                    setIsUnlocked(false);
                    setStatusMessage("UI locked.", "neutral");
                  }}
                >
                  {t("lockUi")}
                </Button>
              )}
              <div className="flex items-center gap-1 rounded-md border border-border bg-card/70 p-1">
                {(["ru", "en", "kz"] as UiLanguage[]).map((code) => (
                  <Button
                    key={code}
                    variant={lang === code ? "default" : "ghost"}
                    size="sm"
                    className="h-7 px-2 text-xs uppercase"
                    onClick={() => setLang(code)}
                    aria-pressed={lang === code}
                    aria-label={`Language ${code.toUpperCase()}`}
                  >
                    {code}
                  </Button>
                ))}
              </div>
              <ThemeToggle theme={theme} onToggle={() => setTheme((current) => (current === "dark" ? "light" : "dark"))} />
            </div>
          </CardContent>
        </Card>

        {status?.monitoringActive && (
          <Card className="border-emerald-500/40 bg-emerald-500/12" role="status" aria-live="polite">
            <CardContent className="py-2.5">
              <p className="text-sm font-medium text-emerald-700 dark:text-emerald-300">
                {t("connectedTeacher")}
              </p>
            </CardContent>
          </Card>
        )}

        {(showSetup || showPairing) && (
          <Card className="overflow-hidden border-border/80 shadow-sm">
            <div className="grid lg:grid-cols-[285px_1fr]">
              <div className="border-b border-border/70 bg-panel-gradient/45 p-4 lg:border-b-0 lg:border-r lg:p-5">
                <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted-foreground">{t("studentConsole")}</p>
                <h2 className="mt-2 text-lg font-semibold">{t("onboardingTitle")}</h2>
                <p className="mt-1 text-sm text-muted-foreground">{t("onboardingDesc")}</p>

                <div className="mt-5 space-y-2" role="list" aria-label="Onboarding steps">
                  <WizardStepItem
                    number={1}
                    title={t("step1Title")}
                    description={t("step1Desc")}
                    state={onboardingStep > 1 ? "done" : onboardingStep === 1 ? "active" : "pending"}
                  />
                  <WizardStepItem
                    number={2}
                    title={t("step2Title")}
                    description={t("step2Desc")}
                    state={onboardingStep > 2 ? "done" : onboardingStep === 2 ? "active" : "pending"}
                  />
                </div>

                <div className="mt-5 rounded-lg border border-border/70 bg-card/65 p-3">
                  <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted-foreground">{t("onboardingHintTitle")}</p>
                  <p className="mt-1 text-sm text-muted-foreground">{t("onboardingHintText")}</p>
                </div>
              </div>

              <div className="p-4 lg:p-5">
                {showSetup ? (
                  <div className="space-y-4">
                    <div>
                      <CardTitle className="text-lg">{t("step1Title")}</CardTitle>
                      <CardDescription className="mt-1">{t("step1Desc")}</CardDescription>
                    </div>

                    <div className="grid gap-3 sm:grid-cols-2">
                      <div className="space-y-2">
                        <label className="text-xs uppercase tracking-[0.12em] text-muted-foreground">{t("adminPassword")}</label>
                        <Input
                          type="password"
                          placeholder={t("adminPassword")}
                          value={setupPassword}
                          onChange={(event) => setSetupPassword(event.target.value)}
                        />
                      </div>
                      <div className="space-y-2">
                        <label className="text-xs uppercase tracking-[0.12em] text-muted-foreground">{t("confirmPassword")}</label>
                        <Input
                          type="password"
                          placeholder={t("confirmPassword")}
                          value={setupConfirm}
                          onChange={(event) => setSetupConfirm(event.target.value)}
                        />
                      </div>
                    </div>

                    <div className="rounded-lg border border-border/70 bg-muted/35 p-3">
                      <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted-foreground">{t("onboardingSecurityChecklist")}</p>
                      <div className="mt-2 space-y-1.5" role="list" aria-label={t("onboardingSecurityChecklist")}>
                        <PasswordRuleItem passed={setupPasswordMinLength} label={t("onboardingMinLength")} />
                        <PasswordRuleItem passed={setupPasswordsMatch} label={t("onboardingPasswordsMatch")} />
                      </div>
                    </div>

                    <label className="flex items-center justify-between gap-3 rounded-lg border border-border/70 bg-background/60 p-3">
                      <span className="text-sm text-muted-foreground">{t("enableAutoStartNow")}</span>
                      <input
                        type="checkbox"
                        className="h-4 w-4 rounded border-border accent-primary"
                        checked={setupAutoStart}
                        onChange={(event) => setSetupAutoStart(event.target.checked)}
                      />
                    </label>

                    <div className="flex justify-end">
                      <Button className="w-full sm:w-auto" onClick={() => setPassword.mutate()} disabled={!canSubmitSetup}>
                        {t("saveAdminPassword")}
                      </Button>
                    </div>
                  </div>
                ) : (
                  <div className="space-y-4">
                    <div>
                      <CardTitle className="text-lg">{t("step2Title")}</CardTitle>
                      <CardDescription className="mt-1">{t("step2Desc")}</CardDescription>
                    </div>

                    <div className="flex flex-wrap items-center gap-2">
                      <Button onClick={() => discover.mutate()} disabled={discover.isPending}>
                        {t("discoverTeachers")}
                      </Button>
                      <Button variant="outline" onClick={() => refreshStatus().catch((error) => setStatusMessage(String(error), "error"))}>
                        {t("refreshStatus")}
                      </Button>
                      <Badge variant={servers.length > 0 ? "success" : "warning"}>
                        {t("onboardingFoundServers")}: {servers.length}
                      </Badge>
                    </div>

                    <p className="text-xs text-muted-foreground">{t("onboardingSelectTargetHint")}</p>

                    <div className="grid gap-3 lg:grid-cols-[1fr_260px]">
                      <div className="space-y-2">
                        <label className="text-xs uppercase tracking-[0.12em] text-muted-foreground">{t("discoveredServer")}</label>
                        <div className="max-h-52 overflow-y-auto rounded-lg border border-border/70 bg-background/60 p-2" role="listbox" aria-label={t("discoveredServer")}>
                          {servers.length === 0 ? (
                            <p className="px-1 py-2 text-sm text-muted-foreground">{t("onboardingNoServers")}</p>
                          ) : (
                            <div className="grid gap-2">
                              {servers.map((server) => {
                                const isSelected = selectedServer === server.baseUrl;
                                return (
                                  <button
                                    key={`${server.serverId}-${server.baseUrl}`}
                                    type="button"
                                    onClick={() => {
                                      setSelectedServer(server.baseUrl);
                                      setManualAddress("");
                                    }}
                                    className={cn(
                                      "rounded-md border px-3 py-2 text-left transition",
                                      isSelected
                                        ? "border-primary bg-primary/10"
                                        : "border-border/80 bg-card/70 hover:border-primary/40 hover:bg-accent/50",
                                    )}
                                    role="option"
                                    aria-selected={isSelected}
                                  >
                                    <div className="flex items-center justify-between gap-2">
                                      <p className="truncate text-sm font-semibold">{server.serverName}</p>
                                      {server.isRecommended ? (
                                        <Badge variant="success" className="shrink-0 text-[10px] uppercase tracking-[0.08em]">
                                          {t("recommendedServer")}
                                        </Badge>
                                      ) : null}
                                    </div>
                                    <p className="mt-1 text-xs text-muted-foreground">{server.host}:{server.port}</p>
                                  </button>
                                );
                              })}
                            </div>
                          )}
                        </div>

                        <div className="rounded-lg border border-border/70 bg-muted/35 p-3">
                          <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted-foreground">{t("onboardingSelectedTarget")}</p>
                          <p className="mt-1 break-all text-sm font-medium">{selectedEndpoint || t("selectDiscovered")}</p>
                          {selectedServerInfo ? (
                            <p className="mt-1 text-xs text-muted-foreground">
                              {selectedServerInfo.serverName} ({selectedServerInfo.host})
                            </p>
                          ) : null}
                        </div>
                      </div>

                      <div className="space-y-3">
                        <div className="rounded-xl border border-primary/45 bg-primary/10 p-3">
                          <label className="text-xs font-semibold uppercase tracking-[0.14em] text-primary">{t("pairingPin")}</label>
                          <Input
                            className="mt-2 h-11 border-primary/50 bg-background/90 text-center text-xl font-semibold tracking-[0.18em]"
                            placeholder="123456"
                            value={pin}
                            onChange={(event) => setPin(event.target.value)}
                            maxLength={12}
                            onKeyDown={(event) => {
                              if (event.key === "Enter" && canSubmitPair) {
                                pair.mutate();
                              }
                            }}
                          />
                          <p className="mt-2 text-xs text-muted-foreground">{t("onboardingEnterPinHint")}</p>
                        </div>

                        <Button className="w-full" onClick={() => pair.mutate()} disabled={!canSubmitPair}>
                          {t("completePairing")}
                        </Button>

                        <div className="rounded-lg border border-dashed border-border/80 bg-card/60 p-3">
                          <button
                            type="button"
                            className="text-xs font-medium text-muted-foreground underline-offset-4 hover:underline"
                            onClick={() => setShowManualAddress((current) => !current)}
                          >
                            {showManualAddress ? t("manualFallbackHide") : t("manualFallbackShow")}
                          </button>
                          {showManualAddress ? (
                            <div className="mt-2 space-y-2">
                              <label className="text-xs uppercase tracking-[0.12em] text-muted-foreground">{t("manualFallback")}</label>
                              <Input
                                placeholder="http://192.168.1.20:40556"
                                value={manualAddress}
                                onChange={(event) => {
                                  setManualAddress(event.target.value);
                                  if (event.target.value.trim().length > 0) {
                                    setSelectedServer("");
                                  }
                                }}
                              />
                              <p className="text-xs text-muted-foreground">{t("manualFallbackHint")}</p>
                            </div>
                          ) : null}
                        </div>
                      </div>
                    </div>
                  </div>
                )}
              </div>
            </div>
          </Card>
        )}

        {showStatus && status && (
          <div className="space-y-2.5">
            <Card className="overflow-hidden border-border/80 bg-panel-gradient/45 shadow-sm">
              <CardContent className="p-3 sm:p-4">
                <div className="grid gap-3 lg:grid-cols-[1.15fr_1fr]">
                  <div className="space-y-3">
                    <div>
                      <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">{t("runtimeStatus")}</p>
                      <p className="mt-1 text-base font-semibold sm:text-lg">
                        {status.pairedServerName ?? "Unknown"}
                      </p>
                      <p className="mt-1 text-xs text-muted-foreground break-all">{status.pairedServerBaseUrl ?? "n/a"}</p>
                    </div>
                    <div className="grid gap-2 sm:grid-cols-2">
                      <StatusTile label={t("serverOnline")} value={status.serverOnline ? t("connected") : t("disconnected")} />
                      <StatusTile label={t("agentProcess")} value={status.agentRunning ? t("running") : t("stopped")} />
                      <StatusTile label={t("autostart")} value={status.agentAutoStart ? t("enabled") : t("disabled")} />
                      <StatusTile label={t("teacherServer")} value={status.pairedServerName ?? "Unknown"} />
                    </div>
                  </div>

                  <div className="rounded-xl border border-border/80 bg-card/70 p-3 sm:p-4">
                    <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted-foreground">{t("agentControls")}</p>
                    <p className="mt-1 text-xs text-muted-foreground">{t("agentControlsDesc")}</p>
                    <div className="mt-3 flex flex-wrap gap-2">
                      <Button onClick={() => startAgent.mutate()} disabled={startAgent.isPending}>
                        {t("startAgent")}
                      </Button>
                      <Button variant="destructive" onClick={() => stopAgent.mutate()} disabled={stopAgent.isPending}>
                        {t("stopAgent")}
                      </Button>
                    </div>
                    <Input
                      className="mt-3"
                      type="password"
                      placeholder={t("passwordForStop")}
                      value={stopPassword}
                      onChange={(event) => setStopPassword(event.target.value)}
                    />
                    <div className="mt-3 rounded-lg border border-border bg-muted/35 p-3">
                      <label className="mb-2 flex items-center gap-2 text-sm">
                        <input
                          type="checkbox"
                          className="h-4 w-4 rounded border-border"
                          checked={autoStart}
                          onChange={(event) => setAutoStart(event.target.checked)}
                        />
                        {t("startAutomatically")}
                      </label>
                      {!autoStart && (
                        <Input
                          type="password"
                          placeholder={t("passwordDisableAutostart")}
                          value={autoStartPassword}
                          onChange={(event) => setAutoStartPassword(event.target.value)}
                        />
                      )}
                      <Button className="mt-3" variant="secondary" onClick={() => saveAutoStart.mutate()} disabled={saveAutoStart.isPending}>
                        {t("saveAutostartPolicy")}
                      </Button>
                    </div>
                  </div>
                </div>
              </CardContent>
            </Card>

            <div className="grid gap-2.5 xl:grid-cols-[1fr_1fr]">
              <Card className="border-border/80 bg-card/90">
                <CardHeader>
                  <CardTitle>{t("deviceName")}</CardTitle>
                  <CardDescription>{t("deviceNameDesc")}</CardDescription>
                </CardHeader>
                <CardContent className="space-y-3">
                  <Input value={deviceName} onChange={(event) => setDeviceName(event.target.value)} />
                  <Input
                    type="password"
                    placeholder={t("passwordForDeviceName")}
                    value={renamePassword}
                    onChange={(event) => setRenamePassword(event.target.value)}
                  />
                  <Button onClick={() => renameDevice.mutate()} disabled={renameDevice.isPending}>
                    {t("saveDeviceName")}
                  </Button>
                  {status.lastAlert ? (
                    <div className="rounded-lg border border-border bg-muted/35 p-3 text-xs text-muted-foreground">
                      <p className="font-medium text-foreground">{t("lastDetectorAlert")}</p>
                      <p className="mt-1 break-words">{status.lastAlert}</p>
                    </div>
                  ) : null}
                </CardContent>
              </Card>

              <Card className="border-border/80 bg-card/90">
                <CardHeader>
                  <CardTitle>{t("securityActions")}</CardTitle>
                  <CardDescription>{t("securityActionsDesc")}</CardDescription>
                </CardHeader>
                <CardContent className="space-y-3">
                  <div className="rounded-lg border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-700 dark:text-amber-300">
                    {t("unpairHint")}
                  </div>
                  <Input
                    type="password"
                    placeholder={t("adminPassword")}
                    value={unpairPassword}
                    onChange={(event) => setUnpairPassword(event.target.value)}
                  />
                  <Button variant="destructive" onClick={() => unpair.mutate()} disabled={unpair.isPending}>
                    {t("unpairTeacher")}
                  </Button>

                  <div className="my-1 h-px bg-border" />
                  <div className="rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive">
                    <p className="font-medium">{t("fullShutdown")}</p>
                    <p className="mt-1 text-xs text-muted-foreground">{t("fullShutdownDesc")}</p>
                  </div>
                  <Input
                    type="password"
                    placeholder={t("passwordForShutdown")}
                    value={shutdownPassword}
                    onChange={(event) => setShutdownPassword(event.target.value)}
                  />
                  <Button
                    variant="destructive"
                    className="w-full"
                    onClick={() => shutdownProgram.mutate()}
                    disabled={shutdownProgram.isPending}
                  >
                    {t("shutdownNow")}
                  </Button>
                </CardContent>
              </Card>
            </div>
          </div>
        )}

        {status && (
          <AccessibilitySettingsPanel
            api={api}
            lang={lang}
            disabled={isLocked}
            onStatusMessage={setStatusMessage}
          />
        )}

        <Card className={cn(
          "border",
          messageTone === "error" && "border-destructive/50 bg-destructive/10",
          messageTone === "success" && "border-emerald-500/50 bg-emerald-500/10",
          messageTone === "neutral" && "border-border bg-card/90",
        )} role={statusMessageRole} aria-live={statusMessageLive} aria-atomic="true">
          <CardContent className="py-3">
            <p className="text-sm">{message}</p>
          </CardContent>
        </Card>
      </main>

      {isLocked && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-background/85 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-labelledby={LOCK_DIALOG_TITLE_ID}
          aria-describedby={LOCK_DIALOG_DESC_ID}
        >
          <Card className="w-full max-w-md border-primary/30 shadow-panel">
            <CardHeader>
              <CardTitle id={LOCK_DIALOG_TITLE_ID}>{t("lockedTitle")}</CardTitle>
              <CardDescription id={LOCK_DIALOG_DESC_ID}>{t("lockedDesc")}</CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              <Input
                type="password"
                placeholder={t("adminPassword")}
                aria-label={t("adminPassword")}
                aria-describedby={LOCK_DIALOG_DESC_ID}
                autoFocus
                value={unlockPassword}
                onChange={(event) => setUnlockPassword(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === "Enter") {
                    unlockUi.mutate();
                  }
                }}
              />
              <Button className="w-full" onClick={() => unlockUi.mutate()} disabled={unlockUi.isPending}>
                {t("unlock")}
              </Button>
            </CardContent>
          </Card>
        </div>
      )}
    </div>
  );
}

type StatusTileProps = {
  label: string;
  value: string;
};

function StatusTile({ label, value }: StatusTileProps) {
  return (
    <div className="rounded-md border border-border/80 bg-background/65 px-2.5 py-2" role="group" aria-label={`${label}: ${value}`}>
      <p className="text-[10px] uppercase tracking-[0.1em] text-muted-foreground">{label}</p>
      <p className="mt-1 truncate text-sm font-medium">{value}</p>
    </div>
  );
}

type WizardStepState = "done" | "active" | "pending";

type WizardStepItemProps = {
  number: number;
  title: string;
  description: string;
  state: WizardStepState;
};

function WizardStepItem({ number, title, description, state }: WizardStepItemProps) {
  return (
    <div
      role="listitem"
      aria-current={state === "active" ? "step" : undefined}
      aria-label={`${title}. ${description}. ${state === "done" ? "Done" : state === "active" ? "Current step" : "Pending"}`}
      className={cn(
        "rounded-lg border p-2.5 transition",
        state === "active" && "border-primary/50 bg-primary/10",
        state === "done" && "border-emerald-500/40 bg-emerald-500/10",
        state === "pending" && "border-border/70 bg-card/65",
      )}
    >
      <div className="flex items-start gap-2.5">
        <div
          className={cn(
            "mt-0.5 inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-xs font-semibold",
            state === "active" && "bg-primary text-primary-foreground",
            state === "done" && "bg-emerald-500 text-white",
            state === "pending" && "bg-muted text-muted-foreground",
          )}
        >
          {state === "done" ? "\u2713" : number}
        </div>
        <div className="min-w-0">
          <p className="text-sm font-semibold leading-5">{title}</p>
          <p className="mt-0.5 text-xs text-muted-foreground">{description}</p>
        </div>
      </div>
    </div>
  );
}

type PasswordRuleItemProps = {
  passed: boolean;
  label: string;
};

function PasswordRuleItem({ passed, label }: PasswordRuleItemProps) {
  return (
    <div className="flex items-center gap-2 text-sm" role="listitem" aria-label={`${label}: ${passed ? "passed" : "not passed"}`}>
      <span
        className={cn(
          "inline-flex h-5 w-5 items-center justify-center rounded-full text-xs font-semibold",
          passed ? "bg-emerald-500/20 text-emerald-600 dark:text-emerald-300" : "bg-muted text-muted-foreground",
        )}
      >
        {passed ? "\u2713" : "\u2022"}
      </span>
      <span className={cn(passed ? "text-emerald-700 dark:text-emerald-300" : "text-muted-foreground")}>{label}</span>
    </div>
  );
}

export default App;

