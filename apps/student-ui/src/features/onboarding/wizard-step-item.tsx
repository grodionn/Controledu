import { cn } from "../../lib/utils";

export type WizardStepState = "done" | "active" | "pending";

type WizardStepItemProps = {
  number: number;
  title: string;
  description: string;
  state: WizardStepState;
};

export function WizardStepItem({ number, title, description, state }: WizardStepItemProps) {
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
