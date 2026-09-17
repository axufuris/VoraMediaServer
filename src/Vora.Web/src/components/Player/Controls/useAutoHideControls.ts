import { useEffect, useState } from 'react';

interface UseAutoHideControlsOptions {
    isMinimized: boolean;
    isPlaying: boolean;
    keepVisibleWhen?: boolean;
    delayMs?: number;
}

export function useAutoHideControls({
    isMinimized,
    isPlaying,
    keepVisibleWhen = false,
    delayMs = 3000
}: UseAutoHideControlsOptions): boolean {
    const [showControls, setShowControls] = useState(true);

    useEffect(() => {
        if (isMinimized) {
            const id = window.setTimeout(() => setShowControls(true), 0);
            return () => window.clearTimeout(id);
        }

        let timeoutId: number;
        const handleMouseMove = () => {
            setShowControls(true);
            window.clearTimeout(timeoutId);
            timeoutId = window.setTimeout(() => {
                if (isPlaying && !keepVisibleWhen) setShowControls(false);
            }, delayMs);
        };

        window.addEventListener('mousemove', handleMouseMove);
        return () => {
            window.removeEventListener('mousemove', handleMouseMove);
            window.clearTimeout(timeoutId);
        };
    }, [isMinimized, isPlaying, keepVisibleWhen, delayMs]);

    return showControls;
}
