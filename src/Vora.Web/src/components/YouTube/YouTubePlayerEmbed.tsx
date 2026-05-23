import { useEffect, useImperativeHandle, useRef, forwardRef, useState } from 'react';

export interface YouTubePlayerHandle {
    play: () => void;
    pause: () => void;
    seekTo: (seconds: number) => void;
    getCurrentTime: () => number;
    getDuration: () => number;
}

interface YouTubePlayerEmbedProps {
    videoId: string;
    onReady?: () => void;
    onStateChange?: (state: 'unstarted' | 'ended' | 'playing' | 'paused' | 'buffering' | 'cued') => void;
    onProgress?: (currentTime: number, duration: number) => void;
}

interface YouTubeIframeApi {
    Player: new (element: HTMLElement, options: YouTubePlayerOptions) => YouTubeIframePlayer;
    PlayerState: {
        UNSTARTED: number;
        ENDED: number;
        PLAYING: number;
        PAUSED: number;
        BUFFERING: number;
        CUED: number;
    };
}

interface YouTubePlayerOptions {
    videoId: string;
    width?: string | number;
    height?: string | number;
    playerVars?: Record<string, number | string>;
    events?: {
        onReady?: () => void;
        onStateChange?: (event: { data: number }) => void;
    };
}

interface YouTubeIframePlayer {
    playVideo: () => void;
    pauseVideo: () => void;
    seekTo: (seconds: number, allowSeekAhead: boolean) => void;
    getCurrentTime: () => number;
    getDuration: () => number;
    getIframe: () => HTMLIFrameElement;
    destroy: () => void;
}

declare global {
    interface Window {
        YT?: YouTubeIframeApi;
        onYouTubeIframeAPIReady?: () => void;
    }
}

const IFRAME_API_SRC = 'https://www.youtube.com/iframe_api';

let apiPromise: Promise<YouTubeIframeApi> | null = null;

function loadIframeApi(): Promise<YouTubeIframeApi> {
    if (apiPromise) return apiPromise;

    apiPromise = new Promise<YouTubeIframeApi>((resolve) => {
        if (window.YT?.Player) {
            resolve(window.YT);
            return;
        }

        const previous = window.onYouTubeIframeAPIReady;
        window.onYouTubeIframeAPIReady = () => {
            previous?.();
            if (window.YT) resolve(window.YT);
        };

        if (!document.querySelector(`script[src="${IFRAME_API_SRC}"]`)) {
            const script = document.createElement('script');
            script.src = IFRAME_API_SRC;
            script.async = true;
            document.body.appendChild(script);
        }
    });

    return apiPromise;
}

const YouTubePlayerEmbed = forwardRef<YouTubePlayerHandle, YouTubePlayerEmbedProps>(function YouTubePlayerEmbed(
    { videoId, onReady, onStateChange, onProgress },
    ref,
) {
    const hostRef = useRef<HTMLDivElement | null>(null);
    const playerRef = useRef<YouTubeIframePlayer | null>(null);
    const progressTimerRef = useRef<number | null>(null);
    const [loadError, setLoadError] = useState<string | null>(null);

    useImperativeHandle(ref, () => ({
        play: () => playerRef.current?.playVideo(),
        pause: () => playerRef.current?.pauseVideo(),
        seekTo: (seconds: number) => playerRef.current?.seekTo(seconds, true),
        getCurrentTime: () => playerRef.current?.getCurrentTime() ?? 0,
        getDuration: () => playerRef.current?.getDuration() ?? 0,
    }), []);

    useEffect(() => {
        let cancelled = false;

        loadIframeApi()
            .then((api) => {
                if (cancelled || !hostRef.current) return;

                playerRef.current?.destroy();
                playerRef.current = new api.Player(hostRef.current, {
                    videoId,
                    width: '100%',
                    height: '100%',
                    playerVars: {
                        autoplay: 1,
                        modestbranding: 1,
                        rel: 0,
                        playsinline: 1,
                    },
                    events: {
                        onReady: () => {
                            const iframe = playerRef.current?.getIframe();
                            if (iframe) {
                                iframe.style.position = 'absolute';
                                iframe.style.top = '0';
                                iframe.style.left = '0';
                                iframe.style.width = '100%';
                                iframe.style.height = '100%';
                                iframe.style.border = '0';
                            }
                            onReady?.();
                            if (progressTimerRef.current) window.clearInterval(progressTimerRef.current);
                            progressTimerRef.current = window.setInterval(() => {
                                const current = playerRef.current?.getCurrentTime() ?? 0;
                                const duration = playerRef.current?.getDuration() ?? 0;
                                if (duration > 0) onProgress?.(current, duration);
                            }, 5000);
                        },
                        onStateChange: (event) => {
                            const state = mapPlayerState(api, event.data);
                            if (state) onStateChange?.(state);
                        },
                    },
                });
            })
            .catch(() => {
                if (!cancelled) setLoadError('Could not load the YouTube player.');
            });

        return () => {
            cancelled = true;
            if (progressTimerRef.current) {
                window.clearInterval(progressTimerRef.current);
                progressTimerRef.current = null;
            }
            playerRef.current?.destroy();
            playerRef.current = null;
        };
    }, [videoId, onReady, onStateChange, onProgress]);

    if (loadError) {
        return (
            <div
                className="flex h-full w-full items-center justify-center text-sm"
                style={{ background: 'var(--vora-bg-sunken)', color: 'var(--vora-text-muted)' }}
            >
                {loadError}
            </div>
        );
    }

    return (
        <div className="relative h-full w-full" style={{ background: 'var(--vora-bg-sunken)' }}>
            <div ref={hostRef} className="absolute inset-0" />
        </div>
    );
});

function mapPlayerState(api: YouTubeIframeApi, value: number): 'unstarted' | 'ended' | 'playing' | 'paused' | 'buffering' | 'cued' | null {
    switch (value) {
        case api.PlayerState.UNSTARTED: return 'unstarted';
        case api.PlayerState.ENDED: return 'ended';
        case api.PlayerState.PLAYING: return 'playing';
        case api.PlayerState.PAUSED: return 'paused';
        case api.PlayerState.BUFFERING: return 'buffering';
        case api.PlayerState.CUED: return 'cued';
        default: return null;
    }
}

export default YouTubePlayerEmbed;
