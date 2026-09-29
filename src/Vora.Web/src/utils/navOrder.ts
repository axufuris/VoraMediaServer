// Moves one sidebar item to a new position and renumbers every item's order
// to match, returning new objects rather than editing the ones in state.
export function moveNavItem<T extends { order: number }>(items: T[], from: number, to: number): T[] {
    const sorted = [...items].sort((a, b) => a.order - b.order);
    if (from < 0 || from >= sorted.length) return sorted;

    const target = Math.max(0, Math.min(to, sorted.length - 1));
    if (target === from) return sorted;

    const [moved] = sorted.splice(from, 1);
    sorted.splice(target, 0, moved);
    return sorted.map((item, index) => ({ ...item, order: index }));
}

// Where a dragged row lands, given the row it's over and whether the pointer is
// in that row's lower half. The insertion point is counted before the dragged
// row is lifted out, so it's shifted back by one when it lies below it.
export function dropIndex(from: number, over: number, after: boolean): number {
    const insertion = after ? over + 1 : over;
    return insertion > from ? insertion - 1 : insertion;
}
