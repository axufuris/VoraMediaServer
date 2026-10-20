import { useEffect, useRef } from 'react';
import type { UpNextItemVM } from '../../api/Media/mediaService';

interface UpNextAutoPlayOptions {
    countingDown: boolean;
    finished: boolean;
    nextItem: UpNextItemVM | null | undefined;
    onPlayNext: (item: UpNextItemVM) => Promise<void>;
}

export function useUpNextAutoPlay({ countingDown, finished, nextItem, onPlayNext }: UpNextAutoPlayOptions): void {
    const startedForRef = useRef<string | null>(null);

    useEffect(() => {
        if (!countingDown || !finished || !nextItem || startedForRef.current === nextItem.id) return;
        startedForRef.current = nextItem.id;
        void onPlayNext(nextItem);
    }, [countingDown, finished, nextItem, onPlayNext]);
}
