// "2.1M", "348K", "912" — a count sized for a chip rather than a ledger. Built on
// Intl so the separator and suffix follow the viewer's locale.
const COMPACT = new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 });

export function formatCompactCount(value: number | null | undefined): string | null {
    if (value == null || !Number.isFinite(value) || value < 0) return null;
    return COMPACT.format(value);
}
