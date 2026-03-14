type StatusTileProps = {
  label: string;
  value: string;
};

export function StatusTile({ label, value }: StatusTileProps) {
  return (
    <div className="rounded-md border border-border/80 bg-background/65 px-2.5 py-2" role="group" aria-label={`${label}: ${value}`}>
      <p className="text-[10px] uppercase tracking-[0.1em] text-muted-foreground">{label}</p>
      <p className="mt-1 truncate text-sm font-medium">{value}</p>
    </div>
  );
}
