import { useState } from 'react';
import { FOCUS_CONTRAST_TEXT, FOCUS_FILL } from './libraryUpdates';

interface SetupInstructionsProps {
    steps: string[];
    defaultOpen: boolean;
}

export default function SetupInstructions({ steps, defaultOpen }: SetupInstructionsProps) {
    const [open, setOpen] = useState(defaultOpen);
    if (steps.length === 0) return null;

    return (
        <section>
            <button
                type="button"
                aria-expanded={open}
                aria-controls="webhook-setup-steps"
                onClick={() => setOpen(value => !value)}
                className={`group flex w-full cursor-pointer items-center justify-between gap-3 rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] px-3 py-2 text-left text-sm font-semibold text-[var(--vora-text-primary)] ${FOCUS_FILL}`}
            >
                Setup instructions
                <svg aria-hidden="true" viewBox="0 0 24 24" className={`h-4 w-4 shrink-0 text-[var(--vora-text-muted)] transition-transform ${FOCUS_CONTRAST_TEXT} ${open ? 'rotate-180' : ''}`} fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
                    <path d="M6 9l6 6 6-6" />
                </svg>
            </button>
            {open && (
                <ol id="webhook-setup-steps" className="mt-3 list-decimal space-y-1 pl-6 text-sm text-[var(--vora-text-secondary)]">
                    {steps.map((step, index) => <li key={index}>{step}</li>)}
                </ol>
            )}
        </section>
    );
}
