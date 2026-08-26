// "1 hr 48 min" / "45 min" — the runtime label shown on every detail hero, so a
// library title and a discovery title read the same.
export function formatRuntime(minutes?: number | null): string | null {
    if (!minutes || minutes <= 0) return null;
    const hours = Math.floor(minutes / 60);
    const mins = Math.round(minutes % 60);
    if (hours === 0) return `${mins} min`;
    return mins === 0 ? `${hours} hr` : `${hours} hr ${mins} min`;
}
