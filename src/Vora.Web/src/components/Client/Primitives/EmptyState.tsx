import type { ReactNode } from 'react';

interface EmptyStateProps {
    icon?: ReactNode;
    title: string;
    description?: string;
    action?: ReactNode;
    className?: string;
}

export default function EmptyState({ icon, title, description, action, className }: EmptyStateProps) {
    return (
        <div className={`flex flex-col items-center justify-center text-center py-16 px-6 ${className ?? ''}`}>
            {icon && (
                <div className="mb-5 opacity-70" style={{ color: 'var(--vora-text-muted)' }}>
                    {icon}
                </div>
            )}
            <h3 className="m-0 text-lg font-semibold" style={{ color: 'var(--vora-text-primary)' }}>
                {title}
            </h3>
            {description && (
                <p className="mt-2 max-w-md text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                    {description}
                </p>
            )}
            {action && <div className="mt-6">{action}</div>}
        </div>
    );
}
