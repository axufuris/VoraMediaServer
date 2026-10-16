import { useEffect, useRef, useState } from 'react';
import { currentPlayerKeySteps, isTypingTarget, nextVolume, playerKeyAction } from '../../../utils/playerKeys';

export type PlayerKeyIndicatorState =
    | { kind: 'seek'; seconds: number }
    | { kind: 'volume'; volume: number };

interface UsePlayerKeyboardOptions {
    enabled: boolean;
    canSeek: boolean;
    volume: number;
    onSkipForward: (seconds: number) => void;
    onSkipBackward: (seconds: number) => void;
    onVolume: (volume: number) => void;
    indicatorMs?: number;
}

const modalIsOpen = () => document.querySelector('[aria-modal="true"]') !== null;

// Left/Right seek and Up/Down change the volume while a full-size player is
// on screen, in a window or fullscreen. The keys are taken from the page, so
// they do not scroll it as well. A text field, an open list or a modal keeps
// its own arrow keys. The steps are read on each press, so a change saved in
// Settings applies to the next press without reopening the player.
//
// Returns what to show for the last press. Presses in the same direction add
// up while it is still showing, so three Rights read +30s, not +10s three
// times.
export function usePlayerKeyboard({
    enabled, canSeek, volume, onSkipForward, onSkipBackward, onVolume, indicatorMs = 900,
}: UsePlayerKeyboardOptions): PlayerKeyIndicatorState | null {
    const [indicator, setIndicator] = useState<PlayerKeyIndicatorState | null>(null);
    const volumeRef = useRef(volume);
    const hideTimer = useRef<number | undefined>(undefined);

    useEffect(() => {
        volumeRef.current = volume;
    }, [volume]);

    useEffect(() => () => window.clearTimeout(hideTimer.current), []);

    useEffect(() => {
        if (!enabled) return;

        const show = (next: (previous: PlayerKeyIndicatorState | null) => PlayerKeyIndicatorState) => {
            setIndicator(next);
            window.clearTimeout(hideTimer.current);
            hideTimer.current = window.setTimeout(() => setIndicator(null), indicatorMs);
        };

        const onKeyDown = (event: KeyboardEvent) => {
            if (event.defaultPrevented || isTypingTarget(event.target) || modalIsOpen()) return;
            const action = playerKeyAction(event, currentPlayerKeySteps(), canSeek);
            if (!action) return;
            event.preventDefault();

            if (action.kind === 'seek') {
                if (action.seconds > 0) onSkipForward(action.seconds);
                else onSkipBackward(-action.seconds);
                show(previous => ({
                    kind: 'seek',
                    seconds: previous?.kind === 'seek' && Math.sign(previous.seconds) === Math.sign(action.seconds)
                        ? previous.seconds + action.seconds
                        : action.seconds,
                }));
                return;
            }

            const level = nextVolume(volumeRef.current, action.percent);
            volumeRef.current = level;
            onVolume(level);
            show(() => ({ kind: 'volume', volume: level }));
        };

        window.addEventListener('keydown', onKeyDown);
        return () => window.removeEventListener('keydown', onKeyDown);
    }, [enabled, canSeek, onSkipForward, onSkipBackward, onVolume, indicatorMs]);

    return indicator;
}
