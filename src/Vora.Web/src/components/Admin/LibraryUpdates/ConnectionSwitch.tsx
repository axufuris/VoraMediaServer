import { FOCUS_CONTRAST_TEXT, FOCUS_FILL } from './libraryUpdates';

interface ConnectionSwitchProps {
    id: string;
    label: string;
    description?: string;
    checked: boolean;
    onChange: (next: boolean) => void;
}

export default function ConnectionSwitch({ id, label, description, checked, onChange }: ConnectionSwitchProps) {
    return (
        <button
            type="button"
            id={id}
            role="switch"
            aria-checked={checked}
            aria-labelledby={`${id}-label`}
            aria-describedby={description ? `${id}-help` : undefined}
            onClick={() => onChange(!checked)}
            className={`group flex w-full cursor-pointer items-start justify-between gap-4 rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] p-3 text-left focus-visible:border-[var(--vora-accent-500)]! ${FOCUS_FILL}`}
        >
            <span className="min-w-0">
                <span id={`${id}-label`} className={`block text-sm font-semibold text-[var(--vora-text-primary)] ${FOCUS_CONTRAST_TEXT}`}>{label}</span>
                {description && (
                    <span id={`${id}-help`} className={`mt-0.5 block text-xs text-[var(--vora-text-muted)] ${FOCUS_CONTRAST_TEXT}`}>{description}</span>
                )}
            </span>
            <span
                aria-hidden="true"
                className={`relative h-7 w-12 shrink-0 rounded-full transition-colors ${checked ? 'bg-[var(--vora-accent-500)] group-focus-visible:bg-[var(--vora-accent-contrast)]' : 'bg-[var(--vora-border-strong)]'}`}
            >
                <span className={`absolute left-1 top-1 h-5 w-5 rounded-full transition-transform ${checked ? 'translate-x-5 bg-[var(--vora-text-primary)] group-focus-visible:bg-[var(--vora-accent-500)]' : 'bg-[var(--vora-text-primary)]'}`} />
            </span>
        </button>
    );
}
