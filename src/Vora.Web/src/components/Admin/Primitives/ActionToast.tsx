import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';

const SHOWN_FOR_MS = 4500;

export function useActionToast(): { toast: ReactNode; showToast: (message: string) => void } {
    const [message, setMessage] = useState<string | null>(null);
    const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

    const showToast = useCallback((next: string) => {
        if (timer.current) clearTimeout(timer.current);
        setMessage(next);
        timer.current = setTimeout(() => setMessage(null), SHOWN_FOR_MS);
    }, []);

    useEffect(() => () => {
        if (timer.current) clearTimeout(timer.current);
    }, []);

    const toast = (
        <div role="status" aria-live="polite" className="pointer-events-none fixed bottom-6 right-6 z-[300] max-w-sm max-md:left-4 max-md:right-4 max-md:max-w-none">
            {message !== null && (
                <div className="pointer-events-auto flex items-start gap-2.5 rounded-[var(--vora-radius-md)] border border-l-4 border-[var(--vora-border-strong)] border-l-[var(--vora-success-500)] bg-[var(--vora-bg-raised)] px-4 py-3 text-sm text-[var(--vora-text-primary)] shadow-lg">
                    <svg className="mt-0.5 h-4 w-4 shrink-0 text-[var(--vora-success-500)]" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M5 13l4 4L19 7" />
                    </svg>
                    <span>{message}</span>
                </div>
            )}
        </div>
    );

    return { toast, showToast };
}
