import type { ReactNode } from 'react';

export function StepHeading({ eyebrow, title, lead }: { eyebrow: string; title: string; lead?: ReactNode }) {
    return (
        <div className="space-y-2">
            <div className="text-[11px] font-bold uppercase tracking-[0.12em] text-[var(--vora-accent-text)]">{eyebrow}</div>
            <h2 className="text-2xl font-semibold tracking-tight text-[var(--vora-text-primary)] [text-wrap:balance]">{title}</h2>
            {lead && <p className="max-w-[64ch] text-[15px] text-[var(--vora-text-secondary)]">{lead}</p>}
        </div>
    );
}

export function WhyNote({ children }: { children: ReactNode }) {
    return (
        <div className="flex max-w-[72ch] gap-3 rounded-[var(--vora-radius-lg)] border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] px-4 py-3 text-sm text-[var(--vora-text-secondary)]">
            <svg className="mt-0.5 h-[18px] w-[18px] shrink-0 text-[var(--vora-accent-text)]" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true"><circle cx="12" cy="12" r="9" /><path d="M12 11v6M12 7.5v.5" /></svg>
            <div>{children}</div>
        </div>
    );
}

export function SetupCard({ children, highlighted = false }: { children: ReactNode; highlighted?: boolean }) {
    return (
        <section className={`vora-card flex min-w-0 flex-col gap-4 p-5 ${highlighted ? '!border-[color-mix(in_srgb,var(--vora-accent-500)_45%,transparent)]' : ''}`}>
            {children}
        </section>
    );
}

export function CardHeading({ title, subtitle, badges }: { title: string; subtitle?: string; badges?: ReactNode }) {
    return (
        <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="min-w-0">
                <h3 className="text-[15px] font-semibold text-[var(--vora-text-primary)]">{title}</h3>
                {subtitle && <p className="mt-0.5 text-[13px] text-[var(--vora-text-muted)]">{subtitle}</p>}
            </div>
            {badges && <div className="flex flex-wrap items-center gap-1.5">{badges}</div>}
        </div>
    );
}

export function RecommendedBadge({ children = 'Recommended' }: { children?: ReactNode }) {
    return (
        <span className="rounded-full bg-[var(--vora-accent-soft)] px-2 py-0.5 text-[10.5px] font-bold uppercase tracking-wider text-[var(--vora-accent-text)]">
            {children}
        </span>
    );
}

export function FieldLabel({ htmlFor, children }: { htmlFor?: string; children: ReactNode }) {
    return <label htmlFor={htmlFor} className="mb-1.5 block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">{children}</label>;
}

export function Hint({ children }: { children: ReactNode }) {
    return <p className="mt-1.5 max-w-[72ch] text-xs text-[var(--vora-text-muted)]">{children}</p>;
}

export function SwitchRow({ id, label, description, checked, onChange }: { id: string; label: string; description?: ReactNode; checked: boolean; onChange: (next: boolean) => void }) {
    return (
        <div className="flex items-start justify-between gap-4">
            <div className="min-w-0">
                <span id={`${id}-label`} className="block font-semibold text-[var(--vora-text-primary)]">{label}</span>
                {description && <span className="text-[13px] text-[var(--vora-text-muted)]">{description}</span>}
            </div>
            <button
                type="button"
                id={id}
                role="switch"
                aria-checked={checked}
                aria-labelledby={`${id}-label`}
                onClick={() => onChange(!checked)}
                className={`relative h-7 w-12 shrink-0 cursor-pointer rounded-full transition-colors ${checked ? 'bg-[var(--vora-accent-500)]' : 'bg-[var(--vora-border-strong)]'}`}
            >
                <span className={`absolute top-1 left-1 h-5 w-5 rounded-full bg-[var(--vora-text-primary)] transition-transform ${checked ? 'translate-x-5' : ''}`} />
            </button>
        </div>
    );
}

export function ChoiceCard({ selected, onSelect, title, description, badge, icon, multiple = false }: {
    selected: boolean;
    onSelect: () => void;
    title: string;
    description: ReactNode;
    badge?: ReactNode;
    icon?: ReactNode;
    multiple?: boolean;
}) {
    return (
        <button
            type="button"
            role={multiple ? undefined : 'radio'}
            aria-checked={multiple ? undefined : selected}
            aria-pressed={multiple ? selected : undefined}
            onClick={onSelect}
            className={`flex min-w-0 cursor-pointer flex-col gap-1.5 rounded-[var(--vora-radius-lg)] border p-4 text-left transition-colors ${selected
                ? 'border-[var(--vora-accent-500)] bg-[var(--vora-accent-soft)]'
                : 'border-[var(--vora-border-strong)] bg-[var(--vora-bg-surface)] hover:border-[var(--vora-text-muted)]'}`}
        >
            {icon && <span className="text-[var(--vora-accent-text)]">{icon}</span>}
            <span className="flex items-center justify-between gap-2 font-semibold text-[var(--vora-text-primary)]">
                <span className="flex flex-wrap items-center gap-2">{title}{badge}</span>
                <span className={`grid h-[18px] w-[18px] shrink-0 place-items-center border-[1.5px] ${multiple ? 'rounded-[5px]' : 'rounded-full'} ${selected
                    ? 'border-[var(--vora-accent-500)] bg-[var(--vora-accent-500)] text-[var(--vora-accent-contrast)]'
                    : 'border-[var(--vora-border-strong)]'}`}>
                    {selected && <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3.5" strokeLinecap="round" aria-hidden="true"><path d="M5 12l5 5L20 7" /></svg>}
                </span>
            </span>
            <span className="text-[13px] text-[var(--vora-text-secondary)]">{description}</span>
        </button>
    );
}

export function CheckRow({ id, checked, onChange, label, hint }: { id: string; checked: boolean; onChange: (next: boolean) => void; label: string; hint?: ReactNode }) {
    return (
        <label htmlFor={id} className="flex cursor-pointer items-start gap-2.5">
            <input id={id} type="checkbox" checked={checked} onChange={e => onChange(e.target.checked)} className="mt-[3px] h-4 w-4 cursor-pointer accent-[var(--vora-accent-500)]" />
            <span className="min-w-0 text-sm text-[var(--vora-text-secondary)]">
                {label}
                {hint && <span className="block text-xs text-[var(--vora-text-muted)]">{hint}</span>}
            </span>
        </label>
    );
}

export function StatusMessage({ tone, children }: { tone: 'ok' | 'error' | 'info'; children: ReactNode }) {
    const color = tone === 'ok' ? 'text-[var(--vora-success-text)]' : tone === 'error' ? 'text-[var(--vora-danger-text)]' : 'text-[var(--vora-info-text)]';
    return <p role={tone === 'error' ? 'alert' : 'status'} className={`flex items-start gap-1.5 text-[13px] ${color}`}>{children}</p>;
}
