interface PageHeaderProps {
    title: string;
    description?: string;
    breadcrumb?: React.ReactNode;
    actions?: React.ReactNode;
    sticky?: boolean;
    children?: React.ReactNode;
}

export default function PageHeader({ title, description, breadcrumb, actions, sticky = true, children }: PageHeaderProps) {
    return (
        <div
            className={`${sticky ? 'sticky top-0 z-20' : ''} vora-page-header`}
        >
            <div className="px-8 py-5">
                {breadcrumb && <div className="mb-2 text-xs text-[var(--vora-text-muted)]">{breadcrumb}</div>}
                <div className="flex items-start justify-between gap-6">
                    <div className="min-w-0">
                        <h1 className="text-2xl font-semibold text-[var(--vora-text-primary)] tracking-tight">{title}</h1>
                        {description && <p className="mt-1 text-sm text-[var(--vora-text-secondary)] max-w-2xl">{description}</p>}
                    </div>
                    {actions && <div className="shrink-0 flex items-center gap-2">{actions}</div>}
                </div>
                {children && <div className="mt-4">{children}</div>}
            </div>
        </div>
    );
}
