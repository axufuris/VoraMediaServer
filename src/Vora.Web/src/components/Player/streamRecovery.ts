export const STREAM_RECOVERY_INTERVAL_MS = 60_000;

export function recoversStream(beforeFirstFrame: boolean, msSinceLastRecovery: number): boolean {
    return !beforeFirstFrame && msSinceLastRecovery >= STREAM_RECOVERY_INTERVAL_MS;
}
