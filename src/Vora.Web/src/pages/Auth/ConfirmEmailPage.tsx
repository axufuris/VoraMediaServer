import { useEffect, useMemo, useRef, useState } from 'react';
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

type Status = 'confirming' | 'success' | 'error' | 'missing';

export default function ConfirmEmailPage() {
    const navigate = useNavigate();
    const [searchParams] = useSearchParams();
    const token = useMemo(() => searchParams.get('token') ?? '', [searchParams]);
    const serverUrl = resolveServerUrl();

    const [status, setStatus] = useState<Status>(token ? 'confirming' : 'missing');
    const [error, setError] = useState('');
    const ran = useRef(false);

    useEffect(() => {
        if (!token || ran.current) return;
        ran.current = true;
        authService.confirmEmailChange(serverUrl, token)
            .then(() => setStatus('success'))
            .catch((err: unknown) => {
                if (isAxiosError(err)) {
                    setError(err.response?.data?.message || 'This confirmation link is invalid or has expired.');
                } else {
                    setError('This confirmation link is invalid or has expired.');
                }
                setStatus('error');
            });
    }, [token, serverUrl]);

    return (
        <AuthLayout title="Vora" subtitle="Confirm email change">
            {status === 'confirming' && (
                <p className="text-sm" style={{ color: 'var(--vora-text-secondary)' }}>Confirming your new email address…</p>
            )}
            {status === 'missing' && (
                <div className="space-y-5">
                    <h2 className="text-xl font-bold" style={{ color: 'var(--vora-text-primary)' }}>Link incomplete</h2>
                    <p className="text-sm" style={{ color: 'var(--vora-text-secondary)' }}>This page expects a confirmation token in the URL. Try copying the link directly from the email.</p>
                    <button type="button" onClick={() => navigate('/login')} className="vora-button-primary w-full cursor-pointer">Go to sign in</button>
                </div>
            )}
            {status === 'success' && (
                <div className="space-y-5">
                    <h2 className="text-xl font-bold" style={{ color: 'var(--vora-text-primary)' }}>Email updated</h2>
                    <p className="text-sm" style={{ color: 'var(--vora-text-secondary)' }}>Your account email has been changed. Please sign in again with your new address.</p>
                    <button type="button" onClick={() => navigate('/login')} className="vora-button-primary w-full cursor-pointer">Go to sign in</button>
                </div>
            )}
            {status === 'error' && (
                <div className="space-y-5">
                    <h2 className="text-xl font-bold" style={{ color: 'var(--vora-text-primary)' }}>Couldn't confirm</h2>
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
                    <button type="button" onClick={() => navigate('/login')} className="vora-button-primary w-full cursor-pointer">Go to sign in</button>
                </div>
            )}
        </AuthLayout>
    );
}
