import type { AuthResponse } from '../../api/Auth/authService';
import { useBrowserSignIn } from '../../hooks/useBrowserSignIn';
import { formatLinkCode } from '../../utils/linkCode';

interface BrowserSignInProps {
    serverUrl: string;
    onApproved: (auth: AuthResponse) => void;
}

const errorBox = { color: 'var(--vora-danger-text)', background: 'var(--vora-danger-soft)', border: '1px solid var(--vora-danger-500)' };
const mutedText = { color: 'var(--vora-text-muted)' };

export default function BrowserSignIn({ serverUrl, onApproved }: BrowserSignInProps) {
    const { state, start, cancel, reopen } = useBrowserSignIn(serverUrl, onApproved);

    if (state.phase === 'waiting') {
        return (
            <div
                role="status"
                className="space-y-3 rounded-lg p-4 text-center"
                style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-strong)' }}
            >
                <p className="text-sm font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Waiting for approval in your browser</p>
                <p className="text-xs" style={mutedText}>
                    {state.browserOpened
                        ? 'Approve this sign-in in the browser that just opened. If it asks you to sign in first, do that, then approve.'
                        : 'Vora could not open your browser. Go to the address below and enter the code.'}
                </p>
                <p
                    aria-label={`Code ${state.userCode}`}
                    className="font-mono text-3xl font-bold tracking-widest"
                    style={{ color: 'var(--vora-accent-text)' }}
                >
                    {formatLinkCode(state.userCode)}
                </p>
                <p className="break-all text-xs" style={mutedText}>{state.verificationUrl}</p>
                <div className="flex gap-3 pt-1">
                    <button type="button" onClick={reopen} className="vora-button-secondary flex-1 cursor-pointer">Open again</button>
                    <button type="button" onClick={cancel} className="vora-button-secondary flex-1 cursor-pointer">Cancel</button>
                </div>
            </div>
        );
    }

    if (state.phase === 'denied' || state.phase === 'expired' || state.phase === 'failed') {
        const message = state.phase === 'denied'
            ? 'That sign-in was declined, so you are not signed in.'
            : state.phase === 'expired'
                ? 'That code expired before it was approved.'
                : state.message;
        return (
            <div className="space-y-3">
                <div role="alert" className="text-sm font-medium p-3 rounded" style={errorBox}>{message}</div>
                <button type="button" onClick={start} className="vora-button-secondary w-full cursor-pointer">
                    {state.phase === 'failed' ? 'Try again' : 'Get a new code'}
                </button>
            </div>
        );
    }

    return (
        <button
            type="button"
            onClick={start}
            disabled={state.phase === 'starting'}
            className="vora-button-secondary flex w-full cursor-pointer items-center justify-center gap-2.5 disabled:opacity-50"
        >
            <svg className="h-5 w-5 shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.75} aria-hidden="true">
                <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 6H18v4.5M18 6l-7.5 7.5M10.5 6H7a2 2 0 00-2 2v9a2 2 0 002 2h9a2 2 0 002-2v-3.5" />
            </svg>
            <span className="truncate">{state.phase === 'starting' ? 'Getting a code…' : 'Sign in with your browser'}</span>
        </button>
    );
}
