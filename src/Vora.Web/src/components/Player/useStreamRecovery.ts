import { useEffect, useEffectEvent, useRef, type RefObject } from 'react';
import { useDialog } from '../../dialogs';
import type { EngineError, PlaybackEngine } from './engine/PlaybackEngine';
import { recoversStream } from './streamRecovery';

export interface RecoverableStream {
    key: string | null;
    startPosition: number;
}

interface StreamRecoveryOptions {
    engine: PlaybackEngine;
    videoRef: RefObject<HTMLVideoElement | null>;
    stream: RecoverableStream;
    restartStream: (position: number) => Promise<void>;
    closePlayer: () => void;
}

export function useStreamRecovery({ engine, videoRef, stream, restartStream, closePlayer }: StreamRecoveryOptions): void {
    const dialog = useDialog();
    const lastRecoveryRef = useRef(Number.NEGATIVE_INFINITY);
    const lastPositionRef = useRef(stream.startPosition);
    const failedRef = useRef(false);
    const waitingRef = useRef<AbortController | null>(null);

    useEffect(() => {
        failedRef.current = false;
        lastPositionRef.current = stream.startPosition;
        const video = videoRef.current;
        if (!video) return;

        const listeners = new AbortController();
        video.addEventListener('timeupdate', () => {
            if (!failedRef.current) lastPositionRef.current = video.currentTime;
        }, { signal: listeners.signal });
        return () => {
            listeners.abort();
            waitingRef.current?.abort();
            waitingRef.current = null;
        };
    }, [videoRef, stream.key, stream.startPosition]);

    const restart = useEffectEvent(() => {
        lastRecoveryRef.current = Date.now();
        void restartStream(lastPositionRef.current);
    });

    const onError = useEffectEvent(async (error: EngineError) => {
        if (failedRef.current) return;
        failedRef.current = true;

        if (!recoversStream(error.beforeFirstFrame, Date.now() - lastRecoveryRef.current)) {
            console.error('Playback failed:', error.message);
            await dialog.alert({
                title: error.beforeFirstFrame ? "This video couldn't play" : 'This video stopped playing',
                message: error.message,
                tone: 'danger',
            });
            closePlayer();
            return;
        }

        const video = videoRef.current;
        if (!video?.paused) {
            restart();
            return;
        }

        const waiting = new AbortController();
        waitingRef.current = waiting;
        video.addEventListener('play', () => {
            waiting.abort();
            if (waitingRef.current === waiting) waitingRef.current = null;
            restart();
        }, { signal: waiting.signal });
    });

    useEffect(() => engine.on(event => {
        if (event.type === 'error') void onError(event.error);
    }), [engine]);
}
