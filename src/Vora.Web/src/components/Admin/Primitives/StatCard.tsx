type StatTone = 'default' | 'accent' | 'success' | 'warn' | 'danger' | 'info';

const TONE_VALUE: Record<StatTone, string> = {
    default: 'text-[var(--vora-text-primary)]',
    accent: 'text-[var(--vora-accent-text)]',
    success: 'text-[var(--vora-success-text)]',
    warn: 'text-[var(--vora-warning-text)]',
    danger: 'text-[var(--vora-danger-text)]',
    info: 'text-[var(--vora-info-text)]',
};

const TONE_RAIL: Record<StatTone, string> = {
    default: 'bg-transparent',
    accent: 'bg-[var(--vora-accent-500)]',
    success: 'bg-[var(--vora-success-500)]',
    warn: 'bg-[var(--vora-warning-500)]',
    danger: 'bg-[var(--vora-danger-500)]',
    info: 'bg-[var(--vora-info-500)]',
};

interface StatCardProps {
    label: string;
    value: string | number;
    unit?: string;
    delta?: { value: string, direction: 'up' | 'down' | 'flat' };
    tone?: StatTone;
    icon?: React.ReactNode;
    footer?: React.ReactNode;
    className?: string;
}

export default function StatCard({ label, value, unit, delta, tone = 'default', icon, footer, className }: StatCardProps) {
    return (
        <div className={`vora-card relative overflow-hidden p-5 ${className ?? ''}`}>
            {tone !== 'default' && (
                <span className={`absolute left-0 top-0 bottom-0 w-1 ${TONE_RAIL[tone]}`} />
            )}
            <div className="flex items-start justify-between mb-3">
                <h3 className="text-xs font-semibold uppercase tracking-wider text-[var(--vora-text-muted)]">{label}</h3>
                {icon && <span className="text-[var(--vora-text-muted)] shrink-0">{icon}</span>}
            </div>
            <div className="flex items-baseline gap-1">
                <div className={`text-3xl font-semibold tabular-nums leading-none ${TONE_VALUE[tone]}`}>{value}</div>
                {unit && <div className="text-sm font-medium text-[var(--vora-text-muted)]">{unit}</div>}
            </div>
            {delta && (
                <div className="mt-2 text-xs font-medium flex items-center gap-1">
                    <span className={delta.direction === 'up' ? 'text-[var(--vora-success-text)]' : delta.direction === 'down' ? 'text-[var(--vora-danger-text)]' : 'text-[var(--vora-text-muted)]'}>
                        {delta.direction === 'up' ? '↗' : delta.direction === 'down' ? '↘' : '→'} {delta.value}
                    </span>
                </div>
            )}
            {footer && <div className="mt-3 pt-3 border-t border-[var(--vora-border-subtle)]">{footer}</div>}
        </div>
    );
}
