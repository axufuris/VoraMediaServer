interface EntityCardProps {
    title: string;
    subtitle?: string;
    badge?: React.ReactNode;
    media?: React.ReactNode;
    footer?: React.ReactNode;
    onClick?: () => void;
    children?: React.ReactNode;
    className?: string;
}

export default function EntityCard({ title, subtitle, badge, media, footer, onClick, children, className }: EntityCardProps) {
    const isInteractive = !!onClick;
    return (
        <div
            onClick={onClick}
            className={`vora-card ${isInteractive ? 'vora-card-interactive cursor-pointer' : ''} flex flex-col ${className ?? ''}`}
            role={isInteractive ? 'button' : undefined}
            tabIndex={isInteractive ? 0 : undefined}
            onKeyDown={isInteractive ? (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onClick?.(); } } : undefined}
        >
            {media && <div className="-m-px mb-0 overflow-hidden rounded-t-[var(--vora-radius-lg)]">{media}</div>}
            <div className="p-5 flex-1 flex flex-col">
                <div className="flex items-start justify-between gap-3 mb-1">
                    <h3 className="text-base font-semibold text-[var(--vora-text-primary)] truncate">{title}</h3>
                    {badge && <div className="shrink-0">{badge}</div>}
                </div>
                {subtitle && <p className="text-sm text-[var(--vora-text-muted)] truncate">{subtitle}</p>}
                {children && <div className="mt-3 flex-1">{children}</div>}
                {footer && <div className="mt-4 pt-3 border-t border-[var(--vora-border-subtle)]">{footer}</div>}
            </div>
        </div>
    );
}
