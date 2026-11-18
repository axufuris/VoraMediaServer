import { useCallback, useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Navigate, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import AuthLayout from '../../layouts/AuthLayout';
import Spinner from '../../components/Common/Spinner';
import { devicePairingService, type DevicePairingRequest } from '../../api/Auth/devicePairingService';
import { serverVault } from '../../utils/serverVault';
import { StorageKeys } from '../../utils/storageKeys';
import { clearPostSignIn, rememberPostSignIn } from '../../utils/postSignInRedirect';
import { resolveReason } from '../../utils/apiError';
import { formatDateTime } from '../../utils/serverTime';
import { LINK_CODE_LENGTH, formatLinkCode, isCompleteLinkCode, normalizeLinkCode } from '../../utils/linkCode';

type Phase = 'enter' | 'looking' | 'confirm' | 'deciding' | 'approved' | 'denied';

function Detail({ label, value }: { label: string; value: string }) {
    return (
        <div className="flex items-baseline justify-between gap-4 py-2" style={{ borderBottom: '1px solid var(--vora-border-subtle)' }}>
            <dt className="text-sm" style={{ color: 'var(--vora-text-muted)' }}>{label}</dt>
            <dd className="text-sm font-semibold text-right m-0" style={{ color: 'var(--vora-text-primary)' }}>{value}</dd>
        </div>
    );
}

export default function LinkDevicePage() {
    const navigate = useNavigate();
    const location = useLocation();
    const [searchParams] = useSearchParams();

    const hasServer = serverVault.getServers().length > 0;
    const hasProfile = !!localStorage.getItem(StorageKeys.profileToken);
    const ready = hasServer && hasProfile;
    const addressCode = searchParams.get('code');
    const codeFromAddress = addressCode && isCompleteLinkCode(addressCode) ? normalizeLinkCode(addressCode) : null;

    const [phase, setPhase] = useState<Phase>(ready && codeFromAddress ? 'looking' : 'enter');
    const [code, setCode] = useState(() => formatLinkCode(addressCode ?? ''));
    const [request, setRequest] = useState<DevicePairingRequest | null>(null);
    const [error, setError] = useState('');

    useEffect(() => {
        if (ready) {
            clearPostSignIn();
        } else {
            rememberPostSignIn(`${location.pathname}${location.search}`);
        }
    }, [ready, location.pathname, location.search]);

    const lookUp = useCallback(async (normalized: string) => {
        try {
            setRequest(await devicePairingService.lookup(normalized));
            setPhase('confirm');
        } catch (err) {
            setError(resolveReason(err) ?? 'Something went wrong. Try again.');
            setPhase('enter');
        }
    }, []);

    useEffect(() => {
        if (!ready || !codeFromAddress) return;
        let cancelled = false;
        devicePairingService.lookup(codeFromAddress).then(
            found => {
                if (cancelled) return;
                setRequest(found);
                setPhase('confirm');
            },
            err => {
                if (cancelled) return;
                setError(resolveReason(err) ?? 'Something went wrong. Try again.');
                setPhase('enter');
            },
        );
        return () => {
            cancelled = true;
        };
    }, [ready, codeFromAddress]);

    const decide = async (approve: boolean) => {
        if (!request) return;
        setError('');
        setPhase('deciding');
        try {
            const answered = normalizeLinkCode(request.userCode);
            await (approve ? devicePairingService.approve(answered) : devicePairingService.deny(answered));
            setPhase(approve ? 'approved' : 'denied');
        } catch (err) {
            setError(resolveReason(err) ?? 'Something went wrong. Try again.');
            setPhase('confirm');
        }
    };

    const submit = (event: FormEvent) => {
        event.preventDefault();
        if (!isCompleteLinkCode(code)) return;
        setError('');
        setPhase('looking');
        void lookUp(normalizeLinkCode(code));
    };

    const startOver = () => {
        setRequest(null);
        setError('');
        setCode('');
        setPhase('enter');
    };

    if (!hasServer) return <Navigate to="/login" replace />;
    if (!hasProfile) return <Navigate to="/profiles" replace />;

    const deviceName = request?.deviceName ?? 'this device';
    const errorBox = error && (
        <div
            role="alert"
            className="text-sm font-medium p-3 rounded"
            style={{ color: 'var(--vora-danger-text)', background: 'var(--vora-danger-soft)', border: '1px solid var(--vora-danger-500)' }}
        >
            {error}
        </div>
    );

    return (
        <AuthLayout title="Vora" subtitle="Link a TV or app">
            {(phase === 'enter' || phase === 'looking') && (
                <form onSubmit={submit} className="space-y-5">
                    <div>
                        <h2 className="text-xl font-bold mb-1" style={{ color: 'var(--vora-text-primary)' }}>Enter the code</h2>
                        <p className="text-sm m-0" style={{ color: 'var(--vora-text-muted)' }}>
                            Open Vora on your TV or app and choose to sign in with a code. Type the code it shows here.
                        </p>
                    </div>
                    <input
                        autoFocus
                        value={code}
                        onChange={event => setCode(formatLinkCode(event.target.value))}
                        inputMode="text"
                        autoCapitalize="characters"
                        autoComplete="off"
                        autoCorrect="off"
                        spellCheck={false}
                        maxLength={LINK_CODE_LENGTH + 1}
                        placeholder="XXXX-XXXX"
                        aria-label="Code shown on your TV or app"
                        className="vora-input w-full text-center font-mono text-2xl tracking-widest"
                    />
                    {errorBox}
                    <button
                        type="submit"
                        disabled={!isCompleteLinkCode(code) || phase === 'looking'}
                        className="vora-button-primary w-full cursor-pointer disabled:opacity-50"
                    >
                        {phase === 'looking' ? 'Checking…' : 'Continue'}
                    </button>
                    {phase === 'looking' && <Spinner size="sm" label="Looking up the code…" />}
                </form>
            )}

            {(phase === 'confirm' || phase === 'deciding') && request && (
                <div className="space-y-5">
                    <h2 className="text-xl font-bold m-0" style={{ color: 'var(--vora-text-primary)' }}>Approve sign-in for {deviceName}?</h2>
                    <dl className="m-0">
                        <Detail label="App" value={request.clientName} />
                        <Detail label="Device" value={`${request.deviceType} · ${request.operatingSystem}`} />
                        <Detail label="Where" value={request.location} />
                        <Detail label="Asked" value={formatDateTime(request.requestedAt)} />
                        <Detail label="Code" value={request.userCode} />
                    </dl>
                    {!request.sameNetwork && (
                        <div
                            className="text-sm font-medium p-3 rounded"
                            style={{ color: 'var(--vora-warning-text)', background: 'var(--vora-warning-soft)', border: '1px solid var(--vora-warning-500)' }}
                        >
                            This device is not on the same network as this browser. Only approve it if it is yours and you started the sign-in yourself.
                        </div>
                    )}
                    {errorBox}
                    <div className="flex gap-3 max-md:flex-col-reverse">
                        <button type="button" disabled={phase === 'deciding'} onClick={() => void decide(false)} className="vora-button-secondary flex-1 cursor-pointer disabled:opacity-50">
                            Deny
                        </button>
                        <button type="button" disabled={phase === 'deciding'} onClick={() => void decide(true)} className="vora-button-primary flex-[2] cursor-pointer disabled:opacity-50">
                            {phase === 'deciding' ? 'Working…' : 'Approve'}
                        </button>
                    </div>
                </div>
            )}

            {phase === 'approved' && (
                <div className="space-y-5 text-center">
                    <h2 className="text-xl font-bold m-0" style={{ color: 'var(--vora-text-primary)' }}>{deviceName} is signed in</h2>
                    <p className="text-sm m-0" style={{ color: 'var(--vora-text-muted)' }}>
                        It is signed in to Vora as you and will pick up in a few seconds. You can close this page.
                    </p>
                    <div className="flex gap-3 max-md:flex-col">
                        <button type="button" onClick={startOver} className="vora-button-secondary flex-1 cursor-pointer">Link another</button>
                        <button type="button" onClick={() => navigate('/')} className="vora-button-primary flex-1 cursor-pointer">Done</button>
                    </div>
                </div>
            )}

            {phase === 'denied' && (
                <div className="space-y-5 text-center">
                    <h2 className="text-xl font-bold m-0" style={{ color: 'var(--vora-text-primary)' }}>Not signed in</h2>
                    <p className="text-sm m-0" style={{ color: 'var(--vora-text-muted)' }}>
                        {deviceName} was turned down and has not been signed in.
                    </p>
                    <div className="flex gap-3 max-md:flex-col">
                        <button type="button" onClick={startOver} className="vora-button-secondary flex-1 cursor-pointer">Enter another code</button>
                        <button type="button" onClick={() => navigate('/')} className="vora-button-primary flex-1 cursor-pointer">Done</button>
                    </div>
                </div>
            )}
        </AuthLayout>
    );
}
