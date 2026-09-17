interface EmptyStateProps {
    title: string;
    description?: string;
    actionLabel?: string;
    onAction?: () => void;
    icon?: React.ReactNode;
    className?: string;
}

export default function EmptyState({ title, description, actionLabel, onAction, icon, className }: EmptyStateProps) {
    return (
        <div className={`flex flex-col items-center justify-center text-center py-12 px-6 ${className ?? ''}`}>
            {icon && (
                <div className="w-12 h-12 rounded-full bg-[var(--vora-bg-sunken)] text-[var(--vora-text-muted)] flex items-center justify-center mb-4">
                    {icon}
                </div>
            )}
            <h3 className="text-base font-semibold text-[var(--vora-text-primary)]">{title}</h3>
            {description && <p className="mt-1 text-sm text-[var(--vora-text-muted)] max-w-sm">{description}</p>}
            {actionLabel && onAction && (
                <button
                    type="button"
                    onClick={onAction}
                    className="mt-4 vora-button-primary"
                >
                    {actionLabel}
                </button>
            )}
        </div>
    );
}
