import { useState, useEffect, useMemo } from 'react';
import { useNavigate, Link, useSearchParams } from 'react-router-dom';
import { isAxiosError } from 'axios';
import { authService } from '../../api/Auth/authService';
import { StorageKeys, SessionKeys } from '../../utils/storageKeys';
import AuthLayout from '../../layouts/AuthLayout';

function resolveServerUrl(): string {
    const envUrl = import.meta.env.VITE_API_BASE_URL;
    if (envUrl) {
        return envUrl.endsWith('/api') ? envUrl.slice(0, -4) : envUrl;
    }
    return window.location.origin;
}

export default function RegisterPage() {
    const navigate = useNavigate();
    const serverUrl = resolveServerUrl();
    const [searchParams] = useSearchParams();
    const inviteToken = useMemo(() => searchParams.get('invite') ?? '', [searchParams]);

    const [registrationMode, setRegistrationMode] = useState<number | null>(null);
    const [isProbing, setIsProbing] = useState(true);
    const [probeError, setProbeError] = useState('');

    const [invitedEmail, setInvitedEmail] = useState<string | null>(null);
    const [inviteError, setInviteError] = useState<string | null>(null);

    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [displayName, setDisplayName] = useState('');
    const [secretCode, setSecretCode] = useState('');
    const [error, setError] = useState('');
    const [isSubmitting, setIsSubmitting] = useState(false);

    useEffect(() => {
        const probe = async () => {
            try {
                const status = await authService.probeServer(serverUrl);
                setRegistrationMode(status.registrationMode);
                sessionStorage.setItem(SessionKeys.pendingServerUrl, serverUrl);

                if (inviteToken) {
                    try {
                        const invite = await authService.validateInvitation(serverUrl, inviteToken);
                        setInvitedEmail(invite.email);
                        setEmail(invite.email);
                    } catch {
                        setInviteError('This invitation is invalid or has expired. Ask the server admin to send a new one.');
                    }
                }
            } catch {
                setProbeError("Could not reach the server. Make sure the Vora backend is running.");
            } finally {
                setIsProbing(false);
            }
        };
        probe();
    }, [serverUrl, inviteToken]);

    const handleRegister = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        if (isSubmitting) return;
        setError('');
        setIsSubmitting(true);
        try {
            const res = await authService.register(email, password, displayName, secretCode || undefined, inviteToken || undefined);

            localStorage.setItem(StorageKeys.accountToken, res.accessToken);
            localStorage.setItem(StorageKeys.userId, res.userId);

            navigate('/profiles');
        } catch (err: unknown) {
            if (isAxiosError(err)) {
                setError(err.response?.data?.message || 'Failed to create account.');
            } else {
                setError('An unexpected error occurred.');
            }
            setIsSubmitting(false);
        }
    };

    if (isProbing) {
        return (
            <AuthLayout title="Vora" subtitle="Connecting to server...">
                <div className="text-sm" style={{ color: 'var(--vora-text-muted)' }}>Probing <span className="font-mono">{serverUrl}</span>...</div>
            </AuthLayout>
        );
    }

    if (probeError) {
        return (
            <AuthLayout title="Vora" subtitle="Server unreachable">
                <div
                    className="text-sm font-medium p-3 rounded mb-4"
                    style={{
                        color: 'var(--vora-warning-text)',
                        background: 'var(--vora-warning-soft)',
                        border: '1px solid var(--vora-warning-500)',
                    }}
                >
                    {probeError}
                </div>
                <div className="text-center text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                    <Link to="/login" className="font-medium transition-colors hover:opacity-80" style={{ color: 'var(--vora-accent-text)' }}>Back to Sign In</Link>
                </div>
            </AuthLayout>
        );
    }

    if (inviteToken && inviteError) {
        return (
            <AuthLayout title="Invitation expired" subtitle="This invite isn't usable anymore.">
                <div
                    className="text-sm font-medium p-3 rounded mb-4"
                    style={{
                        color: 'var(--vora-danger-text)',
                        background: 'var(--vora-danger-soft)',
                        border: '1px solid var(--vora-danger-500)',
                    }}
                >
                    {inviteError}
                </div>
                <div className="text-center text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                    <Link to="/login" className="font-medium transition-colors hover:opacity-80" style={{ color: 'var(--vora-accent-text)' }}>Back to Sign In</Link>
                </div>
            </AuthLayout>
        );
    }

    if (registrationMode === 0 && !invitedEmail) {
        return (
            <AuthLayout title="Registration Disabled" subtitle="The server administrator has closed new account registrations.">
                <Link to="/login" className="vora-button-secondary block text-center cursor-pointer">Return to Login</Link>
            </AuthLayout>
        );
    }

    if (registrationMode === 3 && !invitedEmail) {
        return (
            <AuthLayout title="Invitation Required" subtitle="This server is invite-only.">
                <p className="text-sm mb-4" style={{ color: 'var(--vora-text-secondary)' }}>
                    Ask the server administrator to send you an email invitation. The link in that email will bring you back here with everything pre-filled.
                </p>
                <Link to="/login" className="vora-button-secondary block text-center cursor-pointer">Return to Login</Link>
            </AuthLayout>
        );
    }

    const isInvited = invitedEmail !== null;
    const showSecretCode = !isInvited && registrationMode === 2;

    return (
        <AuthLayout title="Create Account" subtitle={isInvited ? 'You were invited.' : 'Join the Vora server.'}>
            {error && (
                <div
                    className="p-3 rounded mb-6 text-sm"
                    style={{
                        color: 'var(--vora-danger-text)',
                        background: 'var(--vora-danger-soft)',
                        border: '1px solid var(--vora-danger-500)',
                    }}
                >
                    {error}
                </div>
            )}

            <form onSubmit={handleRegister} className="space-y-5">
                {isInvited && (
                    <div
                        className="p-4 rounded-lg mb-2"
                        style={{
                            background: 'var(--vora-accent-soft)',
                            border: '1px solid var(--vora-accent-500)',
                        }}
                    >
                        <p className="text-sm" style={{ color: 'var(--vora-accent-text)' }}>
                            <strong>Invited as</strong> <span className="font-mono">{invitedEmail}</span>
                        </p>
                        <p className="text-xs mt-1" style={{ color: 'var(--vora-text-muted)' }}>
                            Pick a display name and a password to finish setting up your account.
                        </p>
                    </div>
                )}

                {showSecretCode && (
                    <div
                        className="p-4 rounded-lg mb-6"
                        style={{
                            background: 'var(--vora-accent-soft)',
                            border: '1px solid var(--vora-accent-500)',
                        }}
                    >
                        <label className="block text-sm font-bold mb-1" style={{ color: 'var(--vora-accent-text)' }}>Invite PIN</label>
                        <p className="text-xs mb-2" style={{ color: 'var(--vora-text-muted)' }}>Enter the 4-digit PIN provided by the server admin.</p>
                        <input
                            required
                            type="text"
                            inputMode="numeric"
                            pattern="[0-9]{4}"
                            maxLength={4}
                            value={secretCode}
                            onChange={e => setSecretCode(e.target.value.replace(/\D/g, '').slice(0, 4))}
                            className="vora-input w-full text-center text-2xl font-mono tracking-[0.5em]"
                            placeholder="0000"
                        />
                    </div>
                )}

                <div>
                    <label className="block text-sm font-medium mb-1" style={{ color: 'var(--vora-text-muted)' }}>Display Name</label>
                    <input required type="text" value={displayName} onChange={e => setDisplayName(e.target.value)} className="vora-input w-full" placeholder="e.g. Kat" />
                </div>
                <div>
                    <label className="block text-sm font-medium mb-1" style={{ color: 'var(--vora-text-muted)' }}>Email</label>
                    <input
                        required
                        type="email"
                        value={email}
                        onChange={e => setEmail(e.target.value)}
                        className="vora-input w-full disabled:opacity-70 disabled:cursor-not-allowed"
                        disabled={isInvited}
                    />
                    {isInvited && (
                        <p className="text-xs mt-1" style={{ color: 'var(--vora-text-muted)' }}>This must match the email your invitation was sent to.</p>
                    )}
                </div>
                <div>
                    <label className="block text-sm font-medium mb-1" style={{ color: 'var(--vora-text-muted)' }}>Password</label>
                    <input required type="password" minLength={6} value={password} onChange={e => setPassword(e.target.value)} className="vora-input w-full" />
                </div>
                <button type="submit" disabled={isSubmitting} className="vora-button-primary w-full cursor-pointer mt-4 disabled:opacity-70 disabled:cursor-not-allowed">
                    {isSubmitting ? 'Creating account…' : 'Register'}
                </button>
            </form>

            <div className="mt-6 text-center text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                Already have an account? <Link to="/login" className="font-medium transition-colors hover:opacity-80" style={{ color: 'var(--vora-accent-text)' }}>Sign in</Link>
            </div>
        </AuthLayout>
    );
}
