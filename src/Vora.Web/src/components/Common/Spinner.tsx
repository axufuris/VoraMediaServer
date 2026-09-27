type SpinnerSize = 'sm' | 'md' | 'lg';

const DIAMETER: Record<SpinnerSize, string> = {
    sm: '1rem',
    md: '1.75rem',
    lg: '2.5rem',
};

// The one busy indicator. Announces itself, so a screen reader hears that
// something is loading rather than meeting an empty page.
export default function Spinner({ size = 'md', label = 'Loading…', className }: {
    size?: SpinnerSize;
    label?: string;
    className?: string;
}) {
    return (
        <span role="status" aria-live="polite" className={`inline-flex items-center gap-2 ${className ?? ''}`}>
            <span
                aria-hidden="true"
                className="inline-block animate-spin rounded-full"
                style={{
                    width: DIAMETER[size],
                    height: DIAMETER[size],
                    border: '2px solid var(--vora-border-subtle)',
                    borderTopColor: 'var(--vora-accent-500)',
                }}
            />
            <span className="sr-only">{label}</span>
        </span>
    );
}
