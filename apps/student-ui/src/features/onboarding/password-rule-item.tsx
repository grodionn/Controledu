import { cn } from "../../lib/utils";

type PasswordRuleItemProps = {
  passed: boolean;
  label: string;
};

export function PasswordRuleItem({ passed, label }: PasswordRuleItemProps) {
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
