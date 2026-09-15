import type { ReactNode } from 'react';

interface PageHeaderProps {
    title: string;
    // A compact control that belongs to the title itself (e.g. a search toggle)
    // and sits on the same line, directly after it.
    titleAccessory?: ReactNode;
    subtitle?: string;
    actions?: ReactNode;
    eyebrow?: ReactNode;
    backdrop?: ReactNode;
}

export default function PageHeader({ title, titleAccessory, subtitle, actions, eyebrow, backdrop }: PageHeaderProps) {
    return (
        <header className="relative">
            {backdrop && (
                <div className="absolute inset-0 -z-10 overflow-hidden">
                    {backdrop}
                </div>
            )}
            <div className="flex items-end justify-between gap-6 px-8 pt-12 pb-6">
                <div className="min-w-0">
                    {eyebrow && (
                        <div className="mb-3 inline-flex items-center gap-2 text-xs font-medium uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>
                            {eyebrow}
                        </div>
                    )}
                    <div className="flex flex-wrap items-center gap-3">
                        <h1 className="m-0 text-3xl font-semibold" style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.01em' }}>
                            {title}
                        </h1>
                        {titleAccessory}
                    </div>
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
