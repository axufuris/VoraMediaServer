import { type RefObject, useCallback } from 'react';

export function useFullscreen(containerRef: RefObject<HTMLElement | null>): () => Promise<void> {
    return useCallback(async () => {
        if (!document.fullscreenElement) {
            await containerRef.current?.requestFullscreen();
        } else if (document.exitFullscreen) {
            await document.exitFullscreen();
        }
    }, [containerRef]);
}
