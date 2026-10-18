import { useCallback, useEffect, useState, type RefObject } from 'react';

export const EndStretchSeconds = 6;
export const StuckAtTheEndMs = 3000;
const WatchIntervalMs = 500;

export function isStuckAtTheEnd(currentTime: number, duration: number, stillForMs: number): boolean {
    return Number.isFinite(duration) && duration > 0
        && duration - currentTime <= EndStretchSeconds
        && stillForMs >= StuckAtTheEndMs;
}

// Whether the item has finished playing, which is when Up Next takes over.
// The video's own `ended` event is not enough: a transcode whose planned last
// segment holds no video never delivers it, and the player used to sit on the
// last frame. So it also counts as finished when the server says the segment
// is past the end (`markFinished`, from the 410), or when playback has not
// moved for a few seconds right at the end. Seeking back out of the last
// stretch starts it over.
export function usePlaybackFinished(videoRef: RefObject<HTMLVideoElement | null>, playbackKey: string | undefined) {
    const [finishedKey, setFinishedKey] = useState<string | null>(null);

    const markFinished = useCallback(() => {
        if (playbackKey) setFinishedKey(playbackKey);
    }, [playbackKey]);

    useEffect(() => {
        const video = videoRef.current;
        if (!video || !playbackKey) return;

        let lastTime = video.currentTime;
        let lastMovedAt = Date.now();
        const finish = () => setFinishedKey(playbackKey);
        const onSeeking = () => {
            lastMovedAt = Date.now();
            if (!(video.duration - video.currentTime <= EndStretchSeconds)) {
                setFinishedKey(current => (current === playbackKey ? null : current));
            }
        };
        const watch = window.setInterval(() => {
            const now = Date.now();
            if (video.paused || video.seeking || video.currentTime !== lastTime) {
                lastTime = video.currentTime;
                lastMovedAt = now;
                return;
            }
            if (isStuckAtTheEnd(video.currentTime, video.duration, now - lastMovedAt)) finish();
        }, WatchIntervalMs);

        video.addEventListener('ended', finish);
        video.addEventListener('seeking', onSeeking);
        return () => {
            window.clearInterval(watch);
            video.removeEventListener('ended', finish);
            video.removeEventListener('seeking', onSeeking);
        };
    }, [videoRef, playbackKey]);

    return { finished: playbackKey !== undefined && finishedKey === playbackKey, markFinished };
}
