import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { authService } from '../../api/Auth/authService';
import {
    externalSignInService,
    type ExternalComplete,
    type ExternalCompleteRequest,
    type ExternalPending,
} from '../../api/Auth/externalSignInService';
import AuthLayout from '../../layouts/AuthLayout';
import { resolveReason } from '../../utils/apiError';
import { externalErrorMessage, refusalMessage } from '../../utils/externalSignInMessages';
import { storeAccountSession } from '../../utils/signInSession';

function resolveServerUrl(): string {
    const envUrl = import.meta.env.VITE_API_BASE_URL;
    if (envUrl) {
        return envUrl.endsWith('/api') ? envUrl.slice(0, -4) : envUrl;
    }
    return window.location.origin;
}

type View =
    | { kind: 'working'; label: string }
    | { kind: 'failed'; message: string }
    | { kind: 'step'; step: ExternalPending };

const asStep = (result: ExternalPending | ExternalComplete, previous?: ExternalPending): ExternalPending => ({
    outcome: result.outcome,
    refusal: result.refusal,
    message: result.message,
    canCreateAccount: result.canCreateAccount,
    passwordRejected: result.passwordRejected,
    invitePinRejected: result.invitePinRejected,
    providerName: previous?.providerName ?? '',
    email: previous?.email ?? null,
});

const errorBox = { color: 'var(--vora-danger-text)', background: 'var(--vora-danger-soft)', border: '1px solid var(--vora-danger-500)' };
const mutedText = { color: 'var(--vora-text-muted)' };
const labelClass = 'block text-sm font-medium mb-1';

export default function ExternalSignInPage() {
    const navigate = useNavigate();
    const [searchParams] = useSearchParams();
    const serverUrl = resolveServerUrl();
    const started = useRef(false);

    const [view, setView] = useState<View>({ kind: 'working', label: 'Finishing your sign-in…' });
    const [handle, setHandle] = useState('');
    const [busy, setBusy] = useState(false);
    const [stepError, setStepError] = useState('');
    const [linking, setLinking] = useState(false);
    const [linkEmail, setLinkEmail] = useState('');
    const [linkPassword, setLinkPassword] = useState('');
    const [invitePin, setInvitePin] = useState('');

    const finish = useCallback(async (code: string) => {
        setView({ kind: 'working', label: 'Signing you in…' });
        try {
            const auth = await externalSignInService.exchange(serverUrl, code);
            if (!auth.accessToken || !auth.userId) {
                throw new Error('The server returned an unrecognized sign-in.');
            }
            const status = await authService.probeServer(serverUrl).catch(() => null);
            storeAccountSession(serverUrl, status?.serverName, auth);
            navigate('/profiles', { replace: true });
        } catch {
            setView({ kind: 'failed', message: 'That sign-in link has expired or was already used. Start again from the sign-in page.' });
        }
    }, [navigate, serverUrl]);

    useEffect(() => {
        if (started.current) return;
        started.current = true;

        const code = searchParams.get('code');
        const pending = searchParams.get('pending');
        const error = searchParams.get('error');

        if (error) {
            setView({ kind: 'failed', message: externalErrorMessage(error) });
        } else if (code) {
            void finish(code);
        } else if (pending) {
            setHandle(pending);
            externalSignInService.getPending(serverUrl, pending)
                .then(step => {
                    setLinkEmail(step.email ?? '');
                    setView({ kind: 'step', step });
                })
                .catch(() => setView({ kind: 'failed', message: 'That sign-in step expired. Start again from the sign-in page.' }));
        } else {
            setView({ kind: 'failed', message: externalErrorMessage(null) });
        }
    }, [finish, searchParams, serverUrl]);

    const submit = async (request: Omit<ExternalCompleteRequest, 'handle'>, step: ExternalPending) => {
        setBusy(true);
        setStepError('');
        try {
            const result = await externalSignInService.complete(serverUrl, { handle, ...request });
            if (result.outcome === 'SignedIn' && result.code) {
                await finish(result.code);
                return;
            }
            if (result.passwordRejected) setStepError("That email and password don't match an account here.");
            if (result.invitePinRejected) setStepError("That PIN isn't right.");
            if (result.outcome !== 'NeedsCreateOrLink') setLinking(false);
            setView({ kind: 'step', step: asStep(result, step) });
        } catch (err) {
            setStepError(resolveReason(err) ?? "That didn't work. Start again from the sign-in page.");
        } finally {
            setBusy(false);
        }
    };

    if (view.kind === 'working') {
        return (
            <AuthLayout title="Vora" subtitle="Signing in">
                <p className="text-sm" style={mutedText} role="status">{view.label}</p>
            </AuthLayout>
        );
    }

    if (view.kind === 'failed') {
        return (
            <AuthLayout title="Vora" subtitle="Sign-in problem">
                <div role="alert" className="text-sm font-medium p-3 rounded mb-4" style={errorBox}>{view.message}</div>
                <Link to="/login" className="vora-button-secondary block text-center cursor-pointer">Back to Sign In</Link>
            </AuthLayout>
        );
    }

    const { step } = view;
    const provider = step.providerName || 'your provider';

    const linkForm = (
        <form
            onSubmit={e => { e.preventDefault(); void submit({ linkEmail: linkEmail.trim(), linkPassword }, step); }}
            className="space-y-4"
        >
            <div>
                <label htmlFor="link-email" className={labelClass} style={mutedText}>Email of your account here</label>
                <input id="link-email" type="email" required autoFocus value={linkEmail} onChange={e => setLinkEmail(e.target.value)} className="vora-input w-full" />
            </div>
            <div>
                <label htmlFor="link-password" className={labelClass} style={mutedText}>Password</label>
                <input id="link-password" type="password" required value={linkPassword} onChange={e => setLinkPassword(e.target.value)} className="vora-input w-full" />
            </div>
            <button type="submit" disabled={busy} className="vora-button-primary w-full cursor-pointer disabled:opacity-60">
                {busy ? 'Linking…' : `Link ${provider}`}
            </button>
        </form>
    );

    const failure = stepError && <div role="alert" className="text-sm font-medium p-3 rounded mb-4" style={errorBox}>{stepError}</div>;
    const backLink = (
        <div className="mt-6 text-center text-sm" style={mutedText}>
            <Link to="/login" className="font-medium transition-colors hover:opacity-80" style={{ color: 'var(--vora-accent-text)' }}>Back to Sign In</Link>
        </div>
    );

    if (step.outcome === 'Refused') {
        return (
            <AuthLayout title="Vora" subtitle="Can't sign you in">
                <div role="alert" className="text-sm font-medium p-3 rounded mb-4" style={errorBox}>{refusalMessage(step.refusal, step.message)}</div>
                {backLink}
            </AuthLayout>
        );
    }

    if (step.outcome === 'NeedsLinkConfirmation') {
        return (
            <AuthLayout title="Vora" subtitle={`Link ${provider}`}>
                {failure}
                <p className="text-sm mb-5" style={{ color: 'var(--vora-text-secondary)' }}>
                    {step.email ? <>An account with <span className="font-mono">{step.email}</span> already exists on this server.</> : 'An account like yours already exists on this server.'}
                    {' '}Enter its password once to link {provider} to it. After that you can sign in with {provider}.
                </p>
                {linkForm}
                {backLink}
            </AuthLayout>
        );
    }

    if (step.outcome === 'NeedsInvitePin') {
        return (
            <AuthLayout title="Vora" subtitle="One more step">
                {failure}
                <form
                    onSubmit={e => { e.preventDefault(); void submit({ invitePin }, step); }}
                    className="space-y-4"
                >
                    <div className="p-4 rounded-lg" style={{ background: 'var(--vora-accent-soft)', border: '1px solid var(--vora-accent-500)' }}>
                        <label htmlFor="invite-pin" className="block text-sm font-bold mb-1" style={{ color: 'var(--vora-accent-text)' }}>Invite PIN</label>
                        <p className="text-xs mb-2" style={mutedText}>This server asks new people for the 4-digit PIN from the server admin.</p>
                        <input
                            id="invite-pin"
                            required
                            autoFocus
                            inputMode="numeric"
                            pattern="[0-9]{4}"
                            maxLength={4}
                            value={invitePin}
                            onChange={e => setInvitePin(e.target.value.replace(/\D/g, '').slice(0, 4))}
                            className="vora-input w-full text-center text-2xl font-mono tracking-[0.5em]"
                            placeholder="0000"
                        />
                    </div>
                    <button type="submit" disabled={busy || invitePin.length !== 4} className="vora-button-primary w-full cursor-pointer disabled:opacity-60">
                        {busy ? 'Creating your account…' : 'Create my account'}
                    </button>
                </form>
                {backLink}
            </AuthLayout>
        );
    }

    return (
        <AuthLayout title="Vora" subtitle={`Welcome${step.providerName ? `, via ${step.providerName}` : ''}`}>
            {failure}
            {linking || !step.canCreateAccount ? (
                <>
                    <p className="text-sm mb-5" style={{ color: 'var(--vora-text-secondary)' }}>
                        {step.canCreateAccount
                            ? `Sign in to your existing account here to link ${provider} to it.`
                            : `This server doesn't create new accounts from ${provider}. If you already have an account here, sign in to it once to link ${provider}.`}
                    </p>
                    {linkForm}
                    {step.canCreateAccount && (
                        <button type="button" onClick={() => setLinking(false)} className="mt-4 block w-full cursor-pointer text-center text-sm font-medium hover:opacity-80" style={{ color: 'var(--vora-accent-text)' }}>
                            Back
                        </button>
                    )}
                </>
            ) : (
                <div className="space-y-3">
                    <p className="text-sm mb-2" style={{ color: 'var(--vora-text-secondary)' }}>
                        {step.email ? <>You're signed in as <span className="font-mono">{step.email}</span>. </> : null}
                        Is this your first time here?
                    </p>
                    <button type="button" disabled={busy} onClick={() => void submit({ createAccount: true }, step)} className="vora-button-primary w-full cursor-pointer disabled:opacity-60">
                        {busy ? 'Creating your account…' : 'Create a new account'}
                    </button>
                    <button type="button" disabled={busy} onClick={() => setLinking(true)} className="vora-button-secondary w-full cursor-pointer disabled:opacity-60">
                        I already have an account
                    </button>
                </div>
            )}
            {backLink}
        </AuthLayout>
    );
}
