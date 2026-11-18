import { useCallback, useEffect, useRef, useState } from 'react';
import { getResponseStatus } from '../api/client';
import type { AuthResponse } from '../api/Auth/authService';
import { devicePairingService } from '../api/Auth/devicePairingService';
import { openInBrowser } from '../desktop/desktopBrowser';

const MAX_INTERVAL_SECONDS = 30;
const SLOW_DOWN_STEP_SECONDS = 5;
const MAX_LOST_POLLS = 5;

export type BrowserSignInState =
    | { phase: 'idle' }
    | { phase: 'starting' }
    | { phase: 'waiting'; userCode: string; verificationUrl: string; browserOpened: boolean }
    | { phase: 'denied' }
    | { phase: 'expired' }
    | { phase: 'failed'; message: string };

export interface BrowserSignIn {
    state: BrowserSignInState;
    start: () => void;
    cancel: () => void;
    reopen: () => void;
}

type PairingOutcome =
    | { kind: 'approved'; auth: AuthResponse | null | undefined }
    | { kind: 'denied' }
    | { kind: 'expired' }
    | { kind: 'lost' };

const slower = (seconds: number): number => Math.min(seconds + SLOW_DOWN_STEP_SECONDS, MAX_INTERVAL_SECONDS);

function startFailure(error: unknown): string {
    const status = getResponseStatus(error);
    if (status === 403) return 'This computer has been blocked from signing in to this server.';
    if (status === 429) return 'Too many attempts. Wait a few minutes and try again.';
    return 'Could not reach the server. Check the connection and try again.';
}

function watchPairing(serverUrl: string, deviceCode: string, firstSeconds: number, finish: (outcome: PairingOutcome) => void): () => void {
    let stopped = false;
    let timer: number | null = null;

    function wait(seconds: number, lost: number): void {
        timer = window.setTimeout(() => { void poll(seconds, lost); }, seconds * 1000);
    }

    async function poll(seconds: number, lost: number): Promise<void> {
        timer = null;
        if (stopped) return;
        try {
            const result = await devicePairingService.poll(serverUrl, deviceCode);
            if (stopped) return;
            if (result.status === 'approved') {
                finish({ kind: 'approved', auth: result.auth });
            } else if (result.status === 'denied') {
                finish({ kind: 'denied' });
            } else if (result.status === 'expired') {
                finish({ kind: 'expired' });
            } else if (result.status === 'slow_down') {
                wait(Math.min(result.intervalSeconds ?? slower(seconds), MAX_INTERVAL_SECONDS), 0);
            } else {
                wait(seconds, 0);
            }
        } catch (error) {
            if (stopped) return;
            if (getResponseStatus(error) === 429) wait(slower(seconds), lost);
            else if (lost + 1 >= MAX_LOST_POLLS) finish({ kind: 'lost' });
            else wait(seconds, lost + 1);
        }
    }

    wait(firstSeconds, 0);

    return () => {
        stopped = true;
        if (timer !== null) window.clearTimeout(timer);
    };
}

export function useBrowserSignIn(serverUrl: string, onApproved: (auth: AuthResponse) => void): BrowserSignIn {
    const [state, setState] = useState<BrowserSignInState>({ phase: 'idle' });
    const run = useRef(0);
    const watcher = useRef<(() => void) | null>(null);
    const completeUrl = useRef('');
    const approved = useRef(onApproved);

    useEffect(() => {
        approved.current = onApproved;
    }, [onApproved]);

    const stop = useCallback(() => {
        run.current += 1;
        watcher.current?.();
        watcher.current = null;
    }, []);

    useEffect(() => stop, [stop]);

    const start = useCallback(() => {
        stop();
        const id = run.current;
        setState({ phase: 'starting' });
        void (async () => {
            try {
                const pairing = await devicePairingService.start(serverUrl);
                if (id !== run.current) return;
                completeUrl.current = pairing.verificationUrlComplete;
                const opened = await openInBrowser(pairing.verificationUrlComplete);
                if (id !== run.current) return;
                setState({ phase: 'waiting', userCode: pairing.userCode, verificationUrl: pairing.verificationUrl, browserOpened: opened });
                watcher.current = watchPairing(serverUrl, pairing.deviceCode, Math.min(Math.max(pairing.intervalSeconds, 1), MAX_INTERVAL_SECONDS), outcome => {
                    stop();
                    if (outcome.kind === 'approved') {
                        const auth = outcome.auth;
                        if (auth?.accessToken && auth.userId) approved.current(auth);
                        else setState({ phase: 'failed', message: 'The server returned an unrecognized sign-in. Try again.' });
                    } else if (outcome.kind === 'denied') {
                        setState({ phase: 'denied' });
                    } else if (outcome.kind === 'expired') {
                        setState({ phase: 'expired' });
                    } else {
                        setState({ phase: 'failed', message: 'Lost contact with the server while waiting. Try again.' });
                    }
                });
            } catch (error) {
                if (id !== run.current) return;
                setState({ phase: 'failed', message: startFailure(error) });
            }
        })();
    }, [serverUrl, stop]);

    const cancel = useCallback(() => {
        stop();
        setState({ phase: 'idle' });
    }, [stop]);

    const reopen = useCallback(() => {
        const id = run.current;
        void openInBrowser(completeUrl.current).then(opened => {
            if (id !== run.current) return;
            setState(current => (current.phase === 'waiting' ? { ...current, browserOpened: opened } : current));
        });
    }, []);

    return { state, start, cancel, reopen };
}
