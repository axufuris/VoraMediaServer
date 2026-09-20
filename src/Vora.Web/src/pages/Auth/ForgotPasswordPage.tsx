import { useEffect, useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { authService } from '../../api/Auth/authService';
import AuthLayout from '../../layouts/AuthLayout';

function resolveServerUrl(): string {
    const envUrl = import.meta.env.VITE_API_BASE_URL;
    if (envUrl) {
        return envUrl.endsWith('/api') ? envUrl.slice(0, -4) : envUrl;
    }
    return window.location.origin;
}

export default function ForgotPasswordPage() {
    const navigate = useNavigate();
    const serverUrl = resolveServerUrl();

    const [email, setEmail] = useState('');
    const [serverName, setServerName] = useState<string | null>(null);
    const [emailEnabled, setEmailEnabled] = useState<boolean | null>(null);
    const [isSubmitting, setIsSubmitting] = useState(false);
    const [submitted, setSubmitted] = useState(false);

    useEffect(() => {
        let cancelled = false;
        authService.probeServer(serverUrl)
            .then(status => {
                if (cancelled) return;
                setServerName(status.serverName || 'Vora Server');
                setEmailEnabled(status.emailEnabled ?? false);
            })
            .catch(() => {
                if (cancelled) return;
                setEmailEnabled(false);
            });
        return () => { cancelled = true; };
    }, [serverUrl]);

    const handleSubmit = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        if (!email.trim()) return;
        setIsSubmitting(true);
        try {
            await authService.requestPasswordReset(serverUrl, email.trim());
        } catch {
            // intentionally silent — same outcome regardless
        }
        setSubmitted(true);
        setIsSubmitting(false);
    };

    if (emailEnabled === false) {
        return (
            <AuthLayout title="Vora" subtitle={serverName || 'Sign in'}>
                <div className="space-y-5">
                    <h2 className="text-xl font-bold" style={{ color: 'var(--vora-text-primary)' }}>Forgot password</h2>
                    <div
                        className="text-sm font-medium p-3 rounded"
                        style={{
                            color: 'var(--vora-text-secondary)',
                            background: 'var(--vora-bg-sunken)',
                            border: '1px solid var(--vora-border-subtle)',
                        }}
                    >
                        Password reset by email isn't available on this server. Contact your administrator to reset your password.
                    </div>
                    <button type="button" onClick={() => navigate('/login')} className="vora-button-primary w-full cursor-pointer">Back to sign in</button>
                </div>
            </AuthLayout>
        );
    }

    return (
        <AuthLayout title="Vora" subtitle={serverName || 'Sign in'}>
            {submitted ? (
                <div className="space-y-5">
                    <h2 className="text-xl font-bold" style={{ color: 'var(--vora-text-primary)' }}>Check your inbox</h2>
                    <p className="text-sm" style={{ color: 'var(--vora-text-secondary)' }}>
                        If an account exists for <span className="font-medium" style={{ color: 'var(--vora-text-primary)' }}>{email}</span>, we just sent a password reset link. It expires in one hour.
                    </p>
                    <p className="text-xs" style={{ color: 'var(--vora-text-muted)' }}>
                        Didn't get the email? Check your spam folder, or wait a minute and try again — the server limits requests.
                    </p>
                    <button type="button" onClick={() => navigate('/login')} className="vora-button-primary w-full cursor-pointer">Back to sign in</button>
                </div>
            ) : (
                <form onSubmit={handleSubmit} className="space-y-5">
                    <h2 className="text-xl font-bold" style={{ color: 'var(--vora-text-primary)' }}>Forgot password</h2>
                    <p className="text-sm" style={{ color: 'var(--vora-text-secondary)' }}>
                        Enter the email address tied to your account and we'll send you a link to choose a new password.
                    </p>
                    <div>
                        <label className="block text-sm font-medium mb-1" style={{ color: 'var(--vora-text-muted)' }}>Email</label>
                        <input
                            autoFocus
                            type="email"
                            required
                            value={email}
                            onChange={e => setEmail(e.target.value)}
                            className="vora-input w-full"
                        />
                    </div>
                    <div className="pt-2 flex gap-3">
                        <button type="button" onClick={() => navigate('/login')} className="vora-button-secondary flex-1 cursor-pointer">Cancel</button>
                        <button type="submit" disabled={isSubmitting} className="vora-button-primary flex-[2] cursor-pointer disabled:opacity-50">
                            {isSubmitting ? 'Sending…' : 'Send reset link'}
                        </button>
                    </div>
                    <div className="text-center text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                        Remember your password? <Link to="/login" className="font-medium transition-colors hover:opacity-80" style={{ color: 'var(--vora-accent-text)' }}>Sign in</Link>
                    </div>
                </form>
            )}
        </AuthLayout>
    );
}
