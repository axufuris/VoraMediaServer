interface ListCardProps {
    title: string;
    description?: string;
    actions?: React.ReactNode;
    children: React.ReactNode;
    maxBodyHeight?: string;
    className?: string;
}

export default function ListCard({ title, description, actions, children, maxBodyHeight, className }: ListCardProps) {
    return (
        <div className={`vora-card flex flex-col ${className ?? ''}`}>
            <div className="flex items-start justify-between gap-3 px-5 py-4 border-b border-[var(--vora-border-subtle)]">
                <div className="min-w-0">
                    <h3 className="text-sm font-semibold text-[var(--vora-text-primary)]">{title}</h3>
                    {description && <p className="text-xs text-[var(--vora-text-muted)] mt-0.5">{description}</p>}
                </div>
                {actions && <div className="shrink-0 flex items-center gap-2">{actions}</div>}
            </div>
            <div
                className="flex-1 overflow-y-auto"
                style={maxBodyHeight ? { maxHeight: maxBodyHeight } : undefined}
            >
                {children}
            </div>
        </div>
    );
}
