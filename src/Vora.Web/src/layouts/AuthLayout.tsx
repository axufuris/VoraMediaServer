import { type ReactNode } from 'react';

interface AuthLayoutProps {
    title: string;
    subtitle: string;
    children: ReactNode;
}

export default function AuthLayout({ title, subtitle, children }: AuthLayoutProps) {
    return (
        <div
            data-vora-client=""
            className="relative min-h-screen flex flex-col items-center justify-center p-4 overflow-hidden"
            style={{ background: 'var(--vora-bg-canvas)', color: 'var(--vora-text-primary)' }}
        >
            <div
                aria-hidden="true"
                className="pointer-events-none absolute inset-0"
                style={{
                    background:
                        'radial-gradient(ellipse at top, color-mix(in srgb, var(--vora-accent-500) 14%, transparent) 0%, transparent 55%), radial-gradient(ellipse at bottom, color-mix(in srgb, var(--vora-accent-500) 8%, transparent) 0%, transparent 60%)',
                }}
            />

            <div
                className="relative max-w-md w-full p-8 rounded-2xl"
                style={{
                    background: 'color-mix(in srgb, var(--vora-bg-raised) 88%, transparent)',
                    border: '1px solid var(--vora-border-strong)',
                    boxShadow: 'var(--vora-shadow-overlay)',
                    backdropFilter: 'blur(12px)',
                    WebkitBackdropFilter: 'blur(12px)',
                }}
            >
                <div className="text-center mb-8 flex flex-col items-center">
                    <svg width="48" height="48" viewBox="0 0 64 64" className="mb-3" aria-hidden="true">
                        <defs>
                            <linearGradient id="auth-v" x1="0.1" y1="0" x2="0.55" y2="1">
                                <stop offset="0" stopColor="#fdba74" />
                                <stop offset="0.55" stopColor="#fb923c" />
                                <stop offset="1" stopColor="#ea580c" />
                            </linearGradient>
                        </defs>
                        <path d="M6 8 L18 8 L32 40 L46 8 L58 8 L32 60 Z" fill="url(#auth-v)" />
                        <circle cx="52" cy="6" r="2.6" fill="#fbbf24" />
                    </svg>
                    <h1 className="text-3xl font-bold tracking-wider mb-2" style={{ color: 'var(--vora-accent-text)' }}>{title}</h1>
                    <p className="text-sm" style={{ color: 'var(--vora-text-muted)' }}>{subtitle}</p>
                </div>
                {children}
            </div>
        </div>
    );
}
