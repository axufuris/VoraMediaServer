import { useEffect, useRef } from 'react';

export const EndingThresholdSeconds = 15;

interface UpNextEndingOptions {
    // Changing this is what ends one item and starts the next.
    mediaId?: string;
    // False for anything with no "next": live TV, a DVR recording, an extra, or
    // a player the user has minimised out of the way.
    eligible: boolean;
    currentTime: number;
    duration: number;
    // Called once each time the last stretch begins, to refresh what comes next.
    onEnterEnding?: () => void;
}

// Whether playback is inside the last stretch of the item, where the Up Next
// overlay belongs. Derived from the clock rather than stored, so seeking in and
// out of the window just works and there is no flag to get stuck.
//
// The flag it replaces was shared with the up-next fetch that runs when an item
// starts. That fetch set the flag first, so the ending branch behind
// `!flag.current` never ran and the overlay never appeared.
export function useUpNextEnding({ mediaId, eligible, currentTime, duration, onEnterEnding }: UpNextEndingOptions): boolean {
    // Past the end (remaining <= 0) still counts: the overlay has to stay up
    // while the item finishes, which is exactly when the next one matters.
    const isEnding = eligible && duration > 0 && currentTime >= duration - EndingThresholdSeconds;

    const refreshedRef = useRef(false);

    useEffect(() => {
        refreshedRef.current = false;
    }, [mediaId]);

    useEffect(() => {
        if (isEnding && !refreshedRef.current) {
            refreshedRef.current = true;
            onEnterEnding?.();
        } else if (!isEnding && refreshedRef.current) {
            refreshedRef.current = false;
        }
    }, [isEnding, onEnterEnding]);

    return isEnding;
}
