import { useEffect, useEffectEvent, useRef, useState } from 'react';
import { usePlayer, usePlayerTime } from '../../contexts/usePlayer';
import { usePlaybackTransportHost } from '../../contexts/usePlaybackTransport';
import { useDialog } from '../../dialogs';
import type { ChromeIntent, PlayerOutputInfo, VoraDesktopBridge } from '../../desktop/desktopBridge';
import { forceBuiltInPlayer } from '../../desktop/desktopPlayback';
import { NoSubtitle } from '../../utils/subtitleKind';
import { CloseButton } from './Controls/PlayerButtons';
import { DesktopMpvEngine } from './engine/DesktopMpvEngine';
import type { EngineError, EngineEvent } from './engine/PlaybackEngine';
import { buildChromeState, readChromeTheme, type ChromeStatus, type ChromeTheme } from './desktopChromeState';
import { usePlaybackController } from './usePlaybackController';
import { useUpNextAutoPlay } from './useUpNextAutoPlay';

export const CHROME_STATE_INTERVAL_MS = 100;

const Loading: ChromeStatus = { status: 'loading' };

export default function DesktopPlaybackHost({ bridge }: { bridge: VoraDesktopBridge }) {
    const dialog = useDialog();
    const { currentMedia, togglePlayPause, seek, closePlayer } = usePlayer();
    const { currentTime, duration } = usePlayerTime();
    const { attachTransport } = usePlaybackTransportHost();

    const [engine] = useState(() => new DesktopMpvEngine(bridge));
    useEffect(() => attachTransport(engine), [attachTransport, engine]);
    useEffect(() => () => {
        void engine.stop();
        engine.dispose();
    }, [engine]);

    const controller = usePlaybackController(engine);
    const nextItem = controller.upNextData?.nextItem ?? null;
    const previousItem = controller.upNextData?.previousItem ?? null;
    const secondsLeft = controller.finished || !isFinite(duration) ? 0 : Math.max(0, duration - currentTime);

    const countdown = useUpNextAutoPlay({
        countingDown: controller.isEnding && controller.autoPlayNext && !!nextItem,
        finished: controller.finished,
        secondsLeft,
        nextItem,
        onPlayNext: controller.playNext,
    });

    const streamKey = currentMedia?.streamUrl ?? null;
    const [statusFor, setStatusFor] = useState<{ streamKey: string | null; status: ChromeStatus }>({ streamKey, status: Loading });
    const status = statusFor.streamKey === streamKey ? statusFor.status : Loading;
    const [output, setOutput] = useState<PlayerOutputInfo | null>(null);
    const offeringRef = useRef(false);

    const offerBuiltInPlayer = async (error: EngineError) => {
        console.error('The Vora player failed:', error.message);
        const media = currentMedia;
        if (!media || offeringRef.current) return;
        offeringRef.current = true;
        try {
            const useBuiltIn = await dialog.confirm({
                title: error.beforeFirstFrame ? "Couldn't start the Vora player" : 'The Vora player stopped',
                message: error.beforeFirstFrame
                    ? "Couldn't start the Vora player — play with the built-in player instead?"
                    : 'The Vora player stopped — keep watching in the built-in player?',
                confirmText: 'Use the built-in player',
                cancelText: 'Close',
            });
            if (!useBuiltIn) {
                await closePlayer();
                return;
            }
            const restart = controller.restartStream();
            forceBuiltInPlayer();
            void engine.stop();
            await restart;
        } finally {
            offeringRef.current = false;
        }
    };

    const handleIntent = (intent: ChromeIntent) => {
        switch (intent.type) {
            case 'toggle-pause':
                togglePlayPause();
                return;
            case 'seek':
                seek(intent.to);
                return;
            case 'skip-marker':
                if (controller.showSkipCommercial) controller.skipCommercial();
                else controller.skipToMarker();
                return;
            case 'play-next':
                if (nextItem) void controller.playNext(nextItem);
                return;
            case 'play-previous':
                if (previousItem) void controller.playNext(previousItem);
                return;
            case 'cancel-up-next':
                controller.cancelAutoPlay();
                return;
            case 'select-audio':
                void controller.selectAudio(intent.id);
                return;
            case 'select-subtitle':
                void controller.selectSubtitle(intent.id ?? NoSubtitle);
                return;
            case 'select-quality':
                void controller.selectVersion(intent.id);
                return;
            case 'exit':
                void closePlayer();
                return;
        }
    };

    const onEngineEvent = useEffectEvent((event: EngineEvent) => {
        switch (event.type) {
            case 'time':
                if (status.status === 'loading') setStatusFor({ streamKey, status: { status: 'playing' } });
                return;
            case 'buffering':
                if (status.status !== 'error') setStatusFor({ streamKey, status: event.buffering ? Loading : { status: 'playing' } });
                return;
            case 'error':
                setStatusFor({ streamKey, status: { status: 'error', message: event.error.message } });
                void offerBuiltInPlayer(event.error);
                return;
            case 'info':
                setOutput(event.info);
                return;
            case 'intent':
                handleIntent(event.intent);
                return;
            default:
                return;
        }
    });

    useEffect(() => engine.on(event => onEngineEvent(event)), [engine]);

    const barRef = useRef<HTMLDivElement>(null);
    const themeRef = useRef<{ mediaId: string | undefined; theme: ChromeTheme } | null>(null);
    const lastSentRef = useRef('');

    const pushChromeState = useEffectEvent(() => {
        if (!currentMedia) return;
        if (!themeRef.current || themeRef.current.mediaId !== currentMedia.id) {
            themeRef.current = { mediaId: currentMedia.id, theme: readChromeTheme(barRef.current ?? document.body) };
        }
        const state = buildChromeState({ controller, theme: themeRef.current.theme, status, output, countdown });
        const json = JSON.stringify(state);
        if (json === lastSentRef.current) return;
        lastSentRef.current = json;
        engine.setChromeState(state);
    });

    useEffect(() => {
        const timer = window.setInterval(() => pushChromeState(), CHROME_STATE_INTERVAL_MS);
        return () => window.clearInterval(timer);
    }, []);

    const { thumbnails } = controller;
    useEffect(() => {
        if (!thumbnails.available || !thumbnails.spriteUrl) {
            engine.setScrubSprite(null);
            return;
        }
        let cancelled = false;
        fetch(thumbnails.spriteUrl)
            .then(response => response.arrayBuffer())
            .then(image => {
                if (cancelled) return;
                engine.setScrubSprite({
                    image,
                    cueWidth: thumbnails.width,
                    cueHeight: thumbnails.height,
                    cues: thumbnails.cues.map(cue => ({ start: cue.start, end: cue.end, x: cue.x, y: cue.y })),
                });
            })
            .catch((error: unknown) => console.error('Could not read the scrub thumbnails', error));
        return () => { cancelled = true; };
    }, [engine, thumbnails]);

    if (!currentMedia) return null;

    return (
        <div
            ref={barRef}
            className="fixed bottom-0 left-0 right-0 z-[99999] flex h-20 items-center justify-between gap-4 px-6 vora-glass max-md:px-3"
            style={{ borderTop: '1px solid var(--vora-border-subtle)' }}
        >
            <div className="flex min-w-0 flex-col">
                <span role="status" className="text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>
                    {status.status === 'error' ? status.message : 'Playing in the Vora player'}
                </span>
                <span className="truncate font-semibold" style={{ color: 'var(--vora-text-primary)' }}>{currentMedia.title}</span>
                {currentMedia.subtitle && <span className="truncate text-sm" style={{ color: 'var(--vora-text-muted)' }}>{currentMedia.subtitle}</span>}
            </div>
            <CloseButton onClick={closePlayer} />
        </div>
    );
}
