import { useEffect, useRef } from 'react';
import type { UpNextItemVM } from '../../api/Media/mediaService';
import { UP_NEXT_MINIMUM_MS, upNextCountdownSeconds, upNextStartsNow } from './playerPromptTiming';
import { useMsSinceShown } from './useMsSinceShown';

interface UpNextAutoPlayOptions {
    countingDown: boolean;
    finished: boolean;
    secondsLeft: number;
    nextItem: UpNextItemVM | null | undefined;
    onPlayNext: (item: UpNextItemVM) => Promise<void>;
}

export interface UpNextCountdown {
    secondsLeft: number;
    starting: boolean;
}

export function useUpNextAutoPlay({ countingDown, finished, secondsLeft, nextItem, onPlayNext }: UpNextAutoPlayOptions): UpNextCountdown {
    const shownMs = useMsSinceShown(countingDown && nextItem ? nextItem.id : null, UP_NEXT_MINIMUM_MS);
    const starting = countingDown && !!nextItem && upNextStartsNow(finished, shownMs);
    const startedForRef = useRef<string | null>(null);

    useEffect(() => {
        if (!starting || !nextItem || startedForRef.current === nextItem.id) return;
        startedForRef.current = nextItem.id;
        void onPlayNext(nextItem);
    }, [starting, nextItem, onPlayNext]);

    return {
        secondsLeft: countingDown ? upNextCountdownSeconds(secondsLeft, shownMs) : secondsLeft,
        starting,
    };
}
