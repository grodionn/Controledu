import { useEffect, useRef, useState } from "react";
import type { StudentInfo, TeacherStudentChatMessage } from "../lib/types";
import { cn } from "../lib/utils";
import { Badge } from "./ui/badge";
import { Button } from "./ui/button";

type Props = {
  selectedStudent?: StudentInfo;
  messages: TeacherStudentChatMessage[];
  isLoadingHistory?: boolean;
  isSending?: boolean;
  statusText?: string | null;
  statusTone?: "neutral" | "success" | "error" | null;
  onSend: (text: string) => void;
};

export function FocusedChatCard({
  selectedStudent,
  messages,
  isLoadingHistory = false,
  isSending = false,
  statusText,
  statusTone = "neutral",
  onSend,
}: Props) {
  const [draft, setDraft] = useState("");
  const scrollRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const node = scrollRef.current;
    if (!node) {
      return;
    }

    node.scrollTop = node.scrollHeight;
  }, [messages.length, selectedStudent?.clientId]);

  const canSend = Boolean(selectedStudent?.isOnline) && draft.trim().length > 0 && !isSending;

  const submit = () => {
    const text = draft.trim();
    if (!text || !canSend) {
      return;
    }

    onSend(text);
    setDraft("");
  };

  return (
    <section className="rounded-xl border border-border bg-background/75 p-3 shadow-sm">
      <div className="mb-2 flex flex-wrap items-start justify-between gap-2">
        <div>
          <p className="text-xs uppercase tracking-[0.12em] text-muted-foreground">Teacher Chat</p>
          <p className="mt-1 text-xs text-muted-foreground">
            Быстрый чат с выбранным учеником. Сообщения отображаются в overlay на устройстве ученика.
          </p>
        </div>
        {selectedStudent ? (
          <Badge variant={selectedStudent.isOnline ? "success" : "outline"}>{selectedStudent.hostName}</Badge>
        ) : (
          <Badge variant="outline">Select a device</Badge>
        )}
      </div>

      <div className="grid gap-2">
        <div
          ref={scrollRef}
          className="max-h-[360px] min-h-[220px] overflow-y-auto rounded-lg border border-border/80 bg-card/55 p-2"
          role="log"
          aria-live="polite"
        >
          {selectedStudent ? (
            messages.length > 0 ? (
              <div className="grid gap-2">
                {messages.map((message) => {
                  const isTeacher = String(message.senderRole).toLowerCase() === "teacher";
                  return (
                    <div
                      key={message.messageId}
                      className={cn(
                        "max-w-[92%] rounded-lg border px-2.5 py-2 text-xs leading-relaxed",
                        isTeacher
                          ? "ml-auto border-primary/25 bg-primary/10 text-foreground"
                          : "mr-auto border-emerald-500/20 bg-emerald-500/10 text-foreground",
                      )}
                    >
                      <div className="mb-1 flex items-center justify-between gap-3 text-[10px] text-muted-foreground">
                        <span className="truncate">{message.senderDisplayName}</span>
                        <time dateTime={message.timestampUtc}>
                          {new Date(message.timestampUtc).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}
                        </time>
                      </div>
                      <p className="whitespace-pre-wrap break-words">{message.text}</p>
                    </div>
                  );
                })}
              </div>
            ) : (
              <div className="flex h-full min-h-[150px] items-center justify-center rounded-md border border-dashed border-border/70 bg-background/50 px-3 text-center text-xs text-muted-foreground">
                {isLoadingHistory ? "Loading messages..." : "Нет сообщений. Отправьте первое сообщение ученику."}
              </div>
            )
          ) : (
            <div className="flex h-full min-h-[150px] items-center justify-center rounded-md border border-dashed border-border/70 bg-background/50 px-3 text-center text-xs text-muted-foreground">
              Выберите ученика, чтобы открыть чат.
            </div>
          )}
        </div>

        <div className="rounded-lg border border-border/80 bg-card/50 p-2">
          <label className="sr-only" htmlFor="teacher-focused-chat-input">
            Teacher chat message
          </label>
          <textarea
            id="teacher-focused-chat-input"
            className="min-h-[92px] w-full resize-y rounded-md border border-input bg-background px-2.5 py-2 text-sm outline-none ring-offset-background focus-visible:ring-2 focus-visible:ring-primary"
            placeholder="Напишите сообщение ученику..."
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Enter" && !event.shiftKey) {
                event.preventDefault();
                submit();
              }
            }}
            maxLength={2000}
            disabled={!selectedStudent || !selectedStudent.isOnline || isSending}
          />
          <div className="mt-2 flex items-center justify-between gap-2">
            <div className="text-[11px] text-muted-foreground">{draft.length}/2000 • Enter = send</div>
            <Button size="sm" disabled={!canSend} onClick={submit}>
              {isSending ? "Sending..." : "Send"}
            </Button>
          </div>
        </div>

        {statusText ? (
          <div
            className={cn(
              "rounded-md border px-2 py-1.5 text-xs",
              statusTone === "success" && "border-emerald-500/40 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300",
              statusTone === "error" && "border-destructive/40 bg-destructive/10 text-destructive",
              (!statusTone || statusTone === "neutral") && "border-border bg-muted/25 text-muted-foreground",
            )}
          >
            {statusText}
          </div>
        ) : null}
      </div>
    </section>
  );
}
