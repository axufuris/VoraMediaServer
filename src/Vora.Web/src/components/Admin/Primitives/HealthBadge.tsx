import StatusDot from './StatusDot';

type HealthTone = 'ok' | 'warn' | 'error' | 'info' | 'neutral';

const TONE_BG: Record<HealthTone, string> = {
    ok: 'bg-[var(--vora-success-soft)] text-[var(--vora-success-text)]',
    warn: 'bg-[var(--vora-warning-soft)] text-[var(--vora-warning-text)]',
    error: 'bg-[var(--vora-danger-soft)] text-[var(--vora-danger-text)]',
    info: 'bg-[var(--vora-info-soft)] text-[var(--vora-info-text)]',
    neutral: 'bg-[var(--vora-bg-sunken)] text-[var(--vora-text-secondary)]',
};

interface HealthBadgeProps {
    tone?: HealthTone;
    children: React.ReactNode;
    showDot?: boolean;
    className?: string;
}

export default function HealthBadge({ tone = 'neutral', children, showDot = true, className }: HealthBadgeProps) {
    return (
        <span className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-semibold ${TONE_BG[tone]} ${className ?? ''}`}>
            {showDot && <StatusDot tone={tone} />}
            {children}
        </span>
    );
}
