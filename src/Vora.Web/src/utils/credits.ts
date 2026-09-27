// Pull the directing credits out of a cast list. Library items and discovery
// items both carry crew inside the same cast array (the role contains
// "Director"), so both detail pages read the credit the same way.
export function directorsFrom(cast?: { name: string; role: string }[]): string[] {
    return (cast ?? []).filter(member => /director/i.test(member.role)).map(member => member.name);
}

interface DatedCredit {
    releaseDate?: string | null;
    year?: number | null;
}

function releasedAt(credit: DatedCredit): number {
    const parsed = credit.releaseDate ? Date.parse(credit.releaseDate) : NaN;
    if (!Number.isNaN(parsed)) return parsed;
    return credit.year ? Date.UTC(credit.year, 0, 1) : Number.NEGATIVE_INFINITY;
}

// A person's credits, newest release first. Undated ones go last, in the order
// they came.
export function newestFirst<T extends DatedCredit>(credits: T[]): T[] {
    return credits
        .map((credit, index) => ({ credit, index, at: releasedAt(credit) }))
        .sort((a, b) => (a.at === b.at ? a.index - b.index : b.at > a.at ? 1 : -1))
        .map(entry => entry.credit);
}
