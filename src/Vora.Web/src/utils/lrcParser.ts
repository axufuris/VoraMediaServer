export interface LrcLine {
    time: number;
    text: string;
}

const TIMESTAMP_RE = /\[(\d{1,3}):(\d{1,2})(?:\.(\d{1,3}))?\]/g;

export function parseLrc(lrc: string | undefined | null): LrcLine[] {
    if (!lrc) return [];
    const lines: LrcLine[] = [];
    for (const raw of lrc.split(/\r?\n/)) {
        TIMESTAMP_RE.lastIndex = 0;
        const matches: RegExpExecArray[] = [];
        let m: RegExpExecArray | null;
        while ((m = TIMESTAMP_RE.exec(raw)) !== null) matches.push(m);
        if (matches.length === 0) continue;

        const text = raw.replace(TIMESTAMP_RE, '').trim();
        for (const match of matches) {
            const minutes = parseInt(match[1], 10);
            const seconds = parseInt(match[2], 10);
            const fracStr = match[3] ?? '0';
            const fracMs = Math.round(parseFloat('0.' + fracStr) * 1000);
            const time = minutes * 60 + seconds + fracMs / 1000;
            lines.push({ time, text });
        }
    }
    lines.sort((a, b) => a.time - b.time);
    return lines;
}

export function findActiveLineIndex(lines: LrcLine[], currentTime: number): number {
    if (lines.length === 0) return -1;
    let lo = 0;
    let hi = lines.length - 1;
    let result = -1;
    while (lo <= hi) {
        const mid = (lo + hi) >> 1;
        if (lines[mid].time <= currentTime) {
            result = mid;
            lo = mid + 1;
        } else {
            hi = mid - 1;
        }
    }
    return result;
}
