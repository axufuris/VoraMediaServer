// When an item last gained something new. A show or season carries the time
// its newest episode arrived, so it rises to the top when an episode lands in
// it, not only when the show itself was first created. Anything without a
// newer value falls back to when it was added.
export interface RecentlyAddedFields {
    addedAt?: string;
    lastContentAddedAt?: string | null;
}

export function recentlyAddedTime(item: RecentlyAddedFields): number {
    const latest = item.lastContentAddedAt ? Date.parse(item.lastContentAddedAt) : Number.NaN;
    if (!Number.isNaN(latest)) return latest;
    const added = item.addedAt ? Date.parse(item.addedAt) : Number.NaN;
    return Number.isNaN(added) ? 0 : added;
}
