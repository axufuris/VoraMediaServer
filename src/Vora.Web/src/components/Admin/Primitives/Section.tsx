interface SectionProps {
    title?: string;
    description?: string;
    actions?: React.ReactNode;
    children: React.ReactNode;
    className?: string;
}

export default function Section({ title, description, actions, children, className }: SectionProps) {
    return (
        <section className={`space-y-4 ${className ?? ''}`}>
            {(title || actions) && (
                <div className="flex items-end justify-between gap-4">
                    <div>
                        {title && <h2 className="text-base font-semibold text-[var(--vora-text-primary)]">{title}</h2>}
                        {description && <p className="text-sm text-[var(--vora-text-muted)] mt-0.5">{description}</p>}
                    </div>
                    {actions && <div className="shrink-0">{actions}</div>}
                </div>
            )}
            {children}
        </section>
    );
}
