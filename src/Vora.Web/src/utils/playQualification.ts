// Mirrors PlayQualification in Vora.Domain. The server is authoritative and
// applies the same test before storing anything — this copy exists so the player
// does not post a listen that will be thrown away, not so the client gets to
// decide what a play is.
//
// PlayQualificationParityTests fails if these constants drift from the C# ones,
// because a client that is stricter than the server silently loses plays and one
// that is looser just generates rejected requests.
export const LONG_LISTEN_SECONDS = 240;
export const MINIMUM_FRACTION = 0.5;
export const UNKNOWN_DURATION_SECONDS = 30;

export function playQualifies(secondsListened: number, trackDurationSeconds: number): boolean {
    if (secondsListened <= 0) return false;
    if (!(trackDurationSeconds > 0)) return secondsListened >= UNKNOWN_DURATION_SECONDS;

    return secondsListened >= LONG_LISTEN_SECONDS
        || secondsListened / trackDurationSeconds >= MINIMUM_FRACTION;
}
