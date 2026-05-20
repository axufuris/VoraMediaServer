type StatusTone = 'ok' | 'warn' | 'error' | 'info' | 'neutral';

const TONE_BG: Record<StatusTone, string> = {
    ok: 'bg-[var(--vora-success-500)]',
    warn: 'bg-[var(--vora-warning-500)]',
    error: 'bg-[var(--vora-danger-500)]',
    info: 'bg-[var(--vora-info-500)]',
    neutral: 'bg-[var(--vora-text-muted)]',
};

interface StatusDotProps {
    tone?: StatusTone;
    size?: 'sm' | 'md';
    pulse?: boolean;
    className?: string;
    title?: string;
}

export default function StatusDot({ tone = 'neutral', size = 'sm', pulse = false, className, title }: StatusDotProps) {
    const dim = size === 'sm' ? 'w-2 h-2' : 'w-2.5 h-2.5';
    return (
        <span
            title={title}
            className={`inline-block rounded-full shrink-0 ${dim} ${TONE_BG[tone]} ${pulse ? 'animate-pulse' : ''} ${className ?? ''}`}
            aria-hidden={!title}
        />
    );
}
