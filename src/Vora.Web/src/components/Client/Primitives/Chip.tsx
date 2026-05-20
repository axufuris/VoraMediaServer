import type { ReactNode } from 'react';

interface ChipProps {
    children: ReactNode;
    selected?: boolean;
    onClick?: () => void;
    icon?: ReactNode;
    onRemove?: () => void;
    size?: 'sm' | 'md';
    className?: string;
}

export default function Chip({ children, selected, onClick, icon, onRemove, size = 'md', className }: ChipProps) {
    const interactive = !!onClick;
    const padding = size === 'sm' ? '4px 10px' : '6px 14px';
    const fontSize = size === 'sm' ? '12px' : '13px';
    return (
        <button
            type="button"
            disabled={!interactive}
            onClick={onClick}
            className={`inline-flex items-center gap-1.5 rounded-full font-medium transition-colors ${interactive ? 'cursor-pointer' : 'cursor-default'} ${className ?? ''}`}
            style={{
                padding,
                fontSize,
                background: selected ? 'var(--vora-accent-soft)' : 'rgba(255, 255, 255, 0.04)',
                color: selected ? 'var(--vora-accent-text)' : 'var(--vora-text-secondary)',
                border: `1px solid ${selected ? 'var(--vora-accent-soft-hover)' : 'var(--vora-border-subtle)'}`,
            }}
        >
            {icon && <span className="inline-flex items-center" aria-hidden="true">{icon}</span>}
            <span>{children}</span>
            {onRemove && (
                <span
                    role="button"
                    aria-label="Remove"
                    onClick={(e) => { e.stopPropagation(); onRemove(); }}
                    className="ml-1 inline-flex h-4 w-4 cursor-pointer items-center justify-center rounded-full opacity-70 hover:opacity-100"
                >
                    <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                        <line x1="18" y1="6" x2="6" y2="18" />
                        <line x1="6" y1="6" x2="18" y2="18" />
                    </svg>
                </span>
            )}
        </button>
    );
}
