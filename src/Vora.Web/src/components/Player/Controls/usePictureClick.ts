import { useEffect, useRef, useState, type MouseEvent, type PointerEvent } from 'react';
import type { PlayerKeyIndicatorState } from './usePlayerKeyboard';

interface UsePictureClickOptions {
    isPlaying: boolean;
    onPlayPause: () => void;
    indicatorMs?: number;
}

// A mouse click on the picture plays or pauses, as Space does, and shows the
// same indicator. It counts only when the press started and ended on the
// picture itself, so releasing a seek-bar drag over the picture doesn't pause.
// A tap on a touch screen is left to show and hide the controls.
export function usePictureClick({ isPlaying, onPlayPause, indicatorMs = 900 }: UsePictureClickOptions) {
    const [indicator, setIndicator] = useState<PlayerKeyIndicatorState | null>(null);
    const pressedOnPicture = useRef(false);
    const hideTimer = useRef<number | undefined>(undefined);

    useEffect(() => () => window.clearTimeout(hideTimer.current), []);

    const onPointerDown = (event: PointerEvent<HTMLElement>) => {
        pressedOnPicture.current = event.target === event.currentTarget && event.pointerType === 'mouse' && event.button === 0;
    };

    const onClick = (event: MouseEvent<HTMLElement>) => {
        const counts = pressedOnPicture.current && event.target === event.currentTarget;
        pressedOnPicture.current = false;
        if (!counts) return;

        onPlayPause();
        setIndicator({ kind: 'playPause', playing: !isPlaying });
        window.clearTimeout(hideTimer.current);
        hideTimer.current = window.setTimeout(() => setIndicator(null), indicatorMs);
    };

    return { onPointerDown, onClick, indicator };
}
