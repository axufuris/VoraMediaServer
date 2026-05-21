import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { isAxiosError } from 'axios';
import { authService } from '../../api/Auth/authService';
import AuthLayout from '../../layouts/AuthLayout';

function resolveServerUrl(): string {
    const envUrl = import.meta.env.VITE_API_BASE_URL;
    if (envUrl) {
        return envUrl.endsWith('/api') ? envUrl.slice(0, -4) : envUrl;
    }
    return window.location.origin;
}

export default function ResetPasswordPage() {
    const navigate = useNavigate();
    const [searchParams] = useSearchParams();
    const token = useMemo(() => searchParams.get('token') ?? '', [searchParams]);
    const serverUrl = resolveServerUrl();

    const [serverName, setServerName] = useState<string | null>(null);
    const [password, setPassword] = useState('');
    const [confirmPassword, setConfirmPassword] = useState('');
    const [error, setError] = useState('');
    const [isSubmitting, setIsSubmitting] = useState(false);
    const [completed, setCompleted] = useState(false);

    useEffect(() => {
        let cancelled = false;
        authService.probeServer(serverUrl)
            .then(status => {
                if (cancelled) return;
                setServerName(status.serverName || 'Vora Server');
            })
            .catch(() => { /* silent */ });
        return () => { cancelled = true; };
    }, [serverUrl]);

    const handleSubmit = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        setError('');

        if (!token) {
            setError('Reset link is missing the token. Request a new link from the sign-in page.');
            return;
        }
        if (password.length < 6) {
            setError('Password must be at least 6 characters.');
            return;
        }
        if (password !== confirmPassword) {
            setError('Passwords do not match.');
            return;
        }

        setIsSubmitting(true);
        try {
            await authService.confirmPasswordReset(serverUrl, token, password);
            setCompleted(true);
        } catch (err) {
            if (isAxiosError(err)) {
                setError(err.response?.data?.message || err.response?.data || 'Failed to reset password. The link may be invalid or expired.');
            } else {
                setError('Failed to reset password. The link may be invalid or expired.');
            }
        } finally {
            setIsSubmitting(false);
        }
    };

    return (
        <AuthLayout title="Vora" subtitle={serverName || 'Sign in'}>
            {completed ? (
                <div className="space-y-5">
                    <h2 className="text-xl font-bold" style={{ color: 'var(--vora-text-primary)' }}>Password updated</h2>
                    <p className="text-sm" style={{ color: 'var(--vora-text-secondary)' }}>
                        Your password has been reset. You can now sign in with your new password.
                    </p>
                    <button type="button" onClick={() => navigate('/login')} className="vora-button-primary w-full cursor-pointer">Go to sign in</button>
                </div>
            ) : !token ? (
                <div className="space-y-5">
                    <h2 className="text-xl font-bold" style={{ color: 'var(--vora-text-primary)' }}>Reset link incomplete</h2>
                    <p className="text-sm" style={{ color: 'var(--vora-text-secondary)' }}>
                        This page expects a reset token in the URL. If you opened this from an email, the link may have been broken in transit — try copying it directly from the email.
                    </p>
                    <button type="button" onClick={() => navigate('/forgot-password')} className="vora-button-primary w-full cursor-pointer">Request a new link</button>
                </div>
            ) : (
                <form onSubmit={handleSubmit} className="space-y-5">
                    <h2 className="text-xl font-bold" style={{ color: 'var(--vora-text-primary)' }}>Choose a new password</h2>
                    <div>
                        <label className="block text-sm font-medium mb-1" style={{ color: 'var(--vora-text-muted)' }}>New password</label>
                        <input
                            autoFocus
                            type="password"
                            required
                            minLength={6}
                            value={password}
                            onChange={e => setPassword(e.target.value)}
                            className="vora-input w-full"
                            autoComplete="new-password"
                        />
                    </div>
                    <div>
                        <label className="block text-sm font-medium mb-1" style={{ color: 'var(--vora-text-muted)' }}>Confirm new password</label>
                        <input
                            type="password"
                            required
                            minLength={6}
                            value={confirmPassword}
                            onChange={e => setConfirmPassword(e.target.value)}
                            className="vora-input w-full"
                            autoComplete="new-password"
                        />
                    </div>

                    {error && (
                        <div
                            className="text-sm font-medium p-3 rounded"
                            style={{
                                color: 'var(--vora-danger-text)',
                                background: 'var(--vora-danger-soft)',
                                border: '1px solid var(--vora-danger-500)',
                            }}
                        >
                            {error}
                        </div>
                    )}

                    <button type="submit" disabled={isSubmitting} className="vora-button-primary w-full cursor-pointer disabled:opacity-50">
                        {isSubmitting ? 'Updating…' : 'Update password'}
                    </button>
                </form>
            )}
        </AuthLayout>
    );
}
