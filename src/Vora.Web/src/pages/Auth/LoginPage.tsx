import { useState, useEffect } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { isAxiosError } from 'axios';
import { serverVault } from '../../utils/serverVault';
import { authService, type AuthResponse } from '../../api/Auth/authService';
import { externalSignInService, type SignInProvider, type SignInProviders } from '../../api/Auth/externalSignInService';
import SignInProviderButtons, { OrDivider } from '../../components/Auth/SignInProviderButtons';
import BrowserSignIn from '../../components/Auth/BrowserSignIn';
import { canOpenInBrowser } from '../../desktop/desktopBrowser';
import { storeAccountSession } from '../../utils/signInSession';
import { StorageKeys, SessionKeys } from '../../utils/storageKeys';
import AuthLayout from '../../layouts/AuthLayout';

function resolveServerUrl(): string {
    const envUrl = import.meta.env.VITE_API_BASE_URL;
    if (envUrl) {
        return envUrl.endsWith('/api') ? envUrl.slice(0, -4) : envUrl;
    }
    return window.location.origin;
}

export default function LoginPage() {
    const navigate = useNavigate();
    const hasServers = serverVault.getServers().length > 0;
    const serverUrl = resolveServerUrl();

    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [error, setError] = useState('');
    const [isLoading, setIsLoading] = useState(false);
    const [serverName, setServerName] = useState<string | null>(null);
    const [reachable, setReachable] = useState<boolean | null>(null);
    const [emailEnabled, setEmailEnabled] = useState(false);
    const [providers, setProviders] = useState<SignInProviders | null>(null);
    const [adminSignIn, setAdminSignIn] = useState(false);

    // Already signed in? Don't show the login form — send them where
    // RequireAuth would: home if a profile is active, else profile selection.
    // (If the token turns out stale, the API 401 handler bounces back here.)
    useEffect(() => {
        const accountToken = localStorage.getItem(StorageKeys.accountToken);
        const profileToken = localStorage.getItem(StorageKeys.profileToken);
        if (accountToken && profileToken) {
            navigate('/', { replace: true });
        } else if (accountToken) {
            navigate('/profiles', { replace: true });
        }
    }, [navigate]);

    useEffect(() => {
        const probe = async () => {
            try {
                const status = await authService.probeServer(serverUrl);
                setReachable(true);
                setServerName(status.serverName || 'Vora Server');
                setEmailEnabled(status.emailEnabled ?? false);
                externalSignInService.getProviders(serverUrl).then(setProviders).catch(() => setProviders(null));

                if (!status.isClaimed) {
                    sessionStorage.setItem(SessionKeys.pendingServerUrl, serverUrl);
                    navigate('/setup');
                }
            } catch (err) {
                console.debug("Server probe failed:", err);
                setReachable(false);
            }
        };

        probe();
    }, [navigate, serverUrl]);

    const handleLogin = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        setError('');
        setIsLoading(true);

        try {
            const status = await authService.probeServer(serverUrl);
            sessionStorage.setItem(SessionKeys.pendingServerUrl, serverUrl);
            sessionStorage.setItem('pending_server_name', status.serverName || 'Vora Server');

            if (!status.isClaimed) {
                navigate('/setup');
                return;
            }

            const auth = await authService.loginToServer(serverUrl, email, password);
            const { accessToken, userId } = auth;

            if (!accessToken || !userId) {
                throw new Error("Connected successfully, but the server returned an unrecognized token format.");
            }

            sessionStorage.setItem(SessionKeys.pendingUserToken, accessToken);
            sessionStorage.setItem('pending_user_id', userId);

            localStorage.setItem(StorageKeys.accountToken, accessToken);
            localStorage.setItem(StorageKeys.userId, userId);

            navigate('/profiles');
        } catch (err) {
            sessionStorage.removeItem(SessionKeys.pendingServerUrl);
            sessionStorage.removeItem(SessionKeys.pendingUserToken);
            sessionStorage.removeItem('pending_user_id');

            if (isAxiosError(err)) {
                if (err.response?.status === 401) {
                    // Covers wrong credentials and the temporary lockout after
                    // repeated failures — both return 401.
                    setError("Invalid email or password. If you've tried several times, wait a few minutes and try again.");
                } else {
                    // Never hand setError a non-string (e.g. a ProblemDetails object),
                    // or rendering it crashes the page.
                    const data: unknown = err.response?.data;
                    let message = "Failed to connect to server. Check credentials.";
                    if (typeof data === 'string' && data) {
                        message = data;
                    } else if (data && typeof data === 'object') {
                        const d = data as { message?: string; detail?: string; title?: string };
                        message = d.message || d.detail || d.title || message;
                    }
                    setError(message);
                }
            } else if (err instanceof Error) {
                setError(err.message);
            } else {
                setError("An unexpected error occurred.");
            }
        } finally {
            setIsLoading(false);
        }
    };

    const providerList = providers?.providers ?? [];
    const hasProviders = providerList.length > 0;
    const passwordAllowed = !hasProviders || providers?.passwordSignInEnabled !== false;
    const showPasswordForm = passwordAllowed || adminSignIn;

    const chooseProvider = (provider: SignInProvider) => {
        window.location.assign(externalSignInService.startUrl(serverUrl, provider.slug));
    };

    const inDesktop = canOpenInBrowser();

    const signedInInBrowser = (auth: AuthResponse) => {
        storeAccountSession(serverUrl, serverName ?? undefined, auth);
        navigate('/profiles');
    };

    const providerButtons = inDesktop
        ? <BrowserSignIn serverUrl={serverUrl} onApproved={signedInInBrowser} />
        : hasProviders && <SignInProviderButtons providers={providerList} serverUrl={serverUrl} onChoose={chooseProvider} />;

    const unreachable = reachable === false && (
        <div
            className="text-sm font-medium p-3 rounded"
            style={{
                color: 'var(--vora-warning-text)',
                background: 'var(--vora-warning-soft)',
                border: '1px solid var(--vora-warning-500)',
            }}
        >
            Could not reach the server at <span className="font-mono">{serverUrl}</span>. Make sure the Vora backend is running.
        </div>
    );

    return (
        <AuthLayout title="Vora" subtitle={serverName || 'Sign in'}>
            {!passwordAllowed && (
                <div className="space-y-5">
                    <h2 className="text-xl font-bold mb-2" style={{ color: 'var(--vora-text-primary)' }}>Sign In</h2>
                    {unreachable}
                    {providerButtons}
                    {!adminSignIn && (
                        <button
                            type="button"
                            aria-expanded={false}
                            onClick={() => setAdminSignIn(true)}
                            className="block w-full cursor-pointer text-center text-xs font-medium transition-colors hover:opacity-80"
                            style={{ color: 'var(--vora-text-muted)' }}
                        >
                            Admin sign-in
                        </button>
                    )}
                </div>
            )}

            {showPasswordForm && (
            <form onSubmit={handleLogin} className={`space-y-5 ${passwordAllowed ? '' : 'mt-6'}`}>
                {passwordAllowed && <h2 className="text-xl font-bold mb-2" style={{ color: 'var(--vora-text-primary)' }}>Sign In</h2>}
                {!passwordAllowed && (
                    <p className="text-xs" style={{ color: 'var(--vora-text-muted)' }}>
                        Password sign-in is turned off for everyone except admins.
                    </p>
                )}

                {passwordAllowed && unreachable}

                <div>
                    <label htmlFor="login-email" className="block text-sm font-medium mb-1" style={{ color: 'var(--vora-text-muted)' }}>Email</label>
                    <input id="login-email" autoFocus type="email" required value={email} onChange={e => setEmail(e.target.value)} className="vora-input w-full" />
                </div>
                <div>
                    <div className="flex items-baseline justify-between mb-1">
                        <label htmlFor="login-password" className="block text-sm font-medium" style={{ color: 'var(--vora-text-muted)' }}>Password</label>
                        {emailEnabled && (
                            <Link to="/forgot-password" className="text-xs font-medium transition-colors hover:opacity-80" style={{ color: 'var(--vora-accent-text)' }}>Forgot password?</Link>
                        )}
                    </div>
                    <input id="login-password" type="password" required value={password} onChange={e => setPassword(e.target.value)} className="vora-input w-full" />
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

                <div className="pt-2 flex gap-3">
                    {hasServers && (
                        <button type="button" onClick={() => navigate('/')} className="vora-button-secondary flex-1 cursor-pointer">Cancel</button>
                    )}
                    <button type="submit" disabled={isLoading} className="vora-button-primary flex-[2] cursor-pointer disabled:opacity-50">
                        {isLoading ? 'Signing in...' : 'Sign In'}
                    </button>
                </div>
            </form>
            )}

            {passwordAllowed && (hasProviders || inDesktop) && (
                <>
                    <OrDivider />
                    {providerButtons}
                </>
            )}

            <div className="mt-6 text-center text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                Need an account on this server? <Link to="/register" className="font-medium transition-colors hover:opacity-80" style={{ color: 'var(--vora-accent-text)' }}>Register</Link>
            </div>
        </AuthLayout>
    );
}
