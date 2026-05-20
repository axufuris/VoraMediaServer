import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { isAxiosError } from 'axios';
import { authService } from '../../api/Auth/authService';
import AuthLayout from '../../layouts/AuthLayout';

export default function SetupPage() {
    const navigate = useNavigate();
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [displayName, setDisplayName] = useState('');
    const [error, setError] = useState('');

    const getTargetUrl = () => {
        const pending = sessionStorage.getItem('pending_server_url');
        if (pending) return pending;
        const envUrl = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000/api';
        return envUrl.endsWith('/api') ? envUrl.slice(0, -4) : envUrl;
    };

    useEffect(() => {
        const targetUrl = getTargetUrl();
        authService.probeServer(targetUrl)
            .then(status => {
                if (status.isClaimed) navigate('/login');
            })
            .catch(err => {
                console.error("Failed to connect to backend:", err);
                setError("Cannot connect to the server. Is the backend running?");
            });
    }, [navigate]);

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError('');
        try {
            const targetUrl = getTargetUrl();
            const auth = await authService.setupServerAt(targetUrl, email, password, displayName);

            sessionStorage.setItem('pending_server_url', targetUrl);
            sessionStorage.setItem('pending_user_token', auth.accessToken);
            sessionStorage.setItem('pending_user_id', auth.userId);

            localStorage.setItem('account_token', auth.accessToken);
            localStorage.setItem('user_id', auth.userId);

            navigate('/profiles');
        } catch (err) {
            if (isAxiosError(err)) {
                setError(err.response?.data?.message || 'Failed to claim server.');
            } else {
                setError('An unexpected error occurred.');
            }
        }
    };

    return (
        <AuthLayout title="Claim Server" subtitle="Create the master admin account for Vora.">
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

            <form onSubmit={handleSubmit} className="space-y-5">
                <div>
                    <label className="block text-sm font-medium mb-1" style={{ color: 'var(--vora-text-muted)' }}>Display Name</label>
                    <input autoFocus required type="text" value={displayName} onChange={e => setDisplayName(e.target.value)} className="vora-input w-full" placeholder="e.g. Andy" />
                </div>
                <div>
                    <label className="block text-sm font-medium mb-1" style={{ color: 'var(--vora-text-muted)' }}>Email</label>
                    <input required type="email" value={email} onChange={e => setEmail(e.target.value)} className="vora-input w-full" />
                </div>
                <div>
                    <label className="block text-sm font-medium mb-1" style={{ color: 'var(--vora-text-muted)' }}>Password</label>
                    <input required type="password" value={password} onChange={e => setPassword(e.target.value)} className="vora-input w-full" />
                </div>
                <button type="submit" className="vora-button-primary w-full cursor-pointer mt-4">
                    Create Admin Account
                </button>
            </form>
        </AuthLayout>
    );
}
