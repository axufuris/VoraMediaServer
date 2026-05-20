import type { ReactNode } from 'react';

interface PageHeaderProps {
    title: string;
    subtitle?: string;
    actions?: ReactNode;
    eyebrow?: ReactNode;
    backdrop?: ReactNode;
}

export default function PageHeader({ title, subtitle, actions, eyebrow, backdrop }: PageHeaderProps) {
    return (
        <header className="relative">
            {backdrop && (
                <div className="absolute inset-0 -z-10 overflow-hidden">
                    {backdrop}
                </div>
            )}
            <div className="flex items-end justify-between gap-6 px-8 pt-12 pb-6">
                <div>
                    {eyebrow && (
                        <div className="mb-3 inline-flex items-center gap-2 text-xs font-medium uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>
                            {eyebrow}
                        </div>
                    )}
                    <h1 className="m-0 text-3xl font-semibold" style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.01em' }}>
                        {title}
                    </h1>
                    {subtitle && (
                        <p className="mt-2 text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                            {subtitle}
                        </p>
                    )}
                </div>
                {actions && <div className="flex items-center gap-2 shrink-0">{actions}</div>}
            </div>
        </header>
    );
}
