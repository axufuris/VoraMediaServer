// Directional focus movement for remote-control D-pads.
//
// Browsers move focus on Tab, not on arrow keys — there is no spatial
// navigation to inherit. On a TV remote the D-pad sends ArrowUp/Down/Left/Right,
// so without this the focus ring never moves: it sits wherever it landed and
// every OK press activates that same control, which reads as "the arrows keep
// pressing play/pause".
//
// The geometry is kept apart from the DOM wiring because picking the right
// neighbour is the part with the interesting cases — a wide seek bar spanning
// several buttons below it, a row where the nearest control by raw distance is
// diagonally off in another row — and those are worth pinning in tests.

export type Direction = 'up' | 'down' | 'left' | 'right';

export interface NavRect {
    left: number;
    top: number;
    right: number;
    bottom: number;
}

export function isDirection(key: string): key is Direction {
    return key === 'up' || key === 'down' || key === 'left' || key === 'right';
}

export function directionFromKey(key: string): Direction | undefined {
    switch (key) {
        case 'ArrowUp': return 'up';
        case 'ArrowDown': return 'down';
        case 'ArrowLeft': return 'left';
        case 'ArrowRight': return 'right';
        default: return undefined;
    }
}

function center(rect: NavRect): { x: number; y: number } {
    return { x: (rect.left + rect.right) / 2, y: (rect.top + rect.bottom) / 2 };
}

// How far off the travel axis a candidate is, counted as the gap between the
// two rects rather than between their centres. A seek bar spanning the whole
// width overlaps every button below it, so from any of them it reads as
// directly above rather than as far off to one side.
function crossAxisGap(from: NavRect, to: NavRect, direction: Direction): number {
    if (direction === 'up' || direction === 'down') {
        return Math.max(0, from.left - to.right, to.left - from.right);
    }
    return Math.max(0, from.top - to.bottom, to.top - from.bottom);
}

function travelDistance(from: NavRect, to: NavRect, direction: Direction): number {
    const a = center(from);
    const b = center(to);

    switch (direction) {
        case 'up': return a.y - b.y;
        case 'down': return b.y - a.y;
        case 'left': return a.x - b.x;
        case 'right': return b.x - a.x;
    }
}

// A candidate has to actually lie in the direction travelled. Comparing centres
// rather than edges keeps controls that overlap slightly — a taller button next
// to a shorter one — reachable from each other.
const MinimumTravel = 1;

// Cross-axis distance is weighted heavily so movement stays in the row or
// column being travelled: from the shuffle button, Right should reach previous
// rather than jumping diagonally to something closer in raw pixels.
const CrossAxisWeight = 4;

export function scoreCandidate(from: NavRect, to: NavRect, direction: Direction): number | undefined {
    const travel = travelDistance(from, to, direction);
    if (travel < MinimumTravel) return undefined;

    // Anything further off the axis than it is along it is not in the direction
    // pressed in any useful sense. Without this, Right from the last control in
    // a row jumps diagonally up to a header button because it is the only thing
    // to the right at all — which is the unpredictability being complained
    // about. Better to stop at the end of a row than to leap somewhere the
    // viewer did not aim.
    const gap = crossAxisGap(from, to, direction);
    if (gap > travel) return undefined;

    return travel + gap * CrossAxisWeight;
}

export function findNeighbour<T extends { rect: NavRect }>(
    from: NavRect,
    candidates: readonly T[],
    direction: Direction,
): T | undefined {
    let best: T | undefined;
    let bestScore = Number.POSITIVE_INFINITY;

    for (const candidate of candidates) {
        const score = scoreCandidate(from, candidate.rect, direction);
        if (score === undefined || score >= bestScore) continue;
        best = candidate;
        bestScore = score;
    }

    return best;
}

// A focused slider owns its own axis: Left/Right on the seek bar should scrub,
// which is what a viewer expects from a remote, while Up/Down still move off it.
// Text fields keep both axes so caret movement is not stolen.
export function ownsDirection(element: { tagName: string; type?: string }, direction: Direction): boolean {
    const tag = element.tagName.toUpperCase();

    if (tag === 'TEXTAREA') return true;
    if (tag !== 'INPUT') return false;

    const type = (element.type ?? 'text').toLowerCase();
    if (type === 'range') return direction === 'left' || direction === 'right';

    return type === 'text' || type === 'search' || type === 'url' || type === 'email' || type === 'password' || type === 'number';
}
