import { useEffect, type ReactNode } from 'react';

export interface QualityOption<T extends string | number> {
    value: T;
    label: string;
    sublabel?: string;
}

interface QualityPanelSectionProps<T extends string | number> {
    title: string;
    options: QualityOption<T>[];
    value: T;
    onChange: (next: T) => void;
}

export function QualityPanelSection<T extends string | number>({ title, options, value, onChange }: QualityPanelSectionProps<T>) {
    return (
        <section className="px-5 py-4 border-b" style={{ borderColor: 'var(--vora-border-subtle)' }}>
            <h3 className="m-0 mb-3 text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>
                {title}
            </h3>
            <div className="flex flex-col gap-1">
                {options.map(opt => {
                    const selected = opt.value === value;
                    return (
                        <button
                            key={String(opt.value)}
                            type="button"
                            onClick={() => onChange(opt.value)}
                            className="flex cursor-pointer items-center justify-between rounded-md px-3 py-2 text-left transition-colors"
                            style={{
                                background: selected ? 'var(--vora-accent-soft)' : 'transparent',
                                color: selected ? 'var(--vora-accent-text)' : 'var(--vora-text-primary)',
                            }}
                        >
                            <span className="flex flex-col">
                                <span className="text-sm font-medium">{opt.label}</span>
                                {opt.sublabel && <span className="text-xs" style={{ color: 'var(--vora-text-muted)' }}>{opt.sublabel}</span>}
                            </span>
                            {selected && (
                                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                    <polyline points="20 6 9 17 4 12" />
                                </svg>
                            )}
                        </button>
                    );
                })}
            </div>
        </section>
    );
}

interface QualityPanelProps {
    open: boolean;
    onClose: () => void;
    title?: string;
    children: ReactNode;
}

export default function QualityPanel({ open, onClose, title = 'Quality & tracks', children }: QualityPanelProps) {
    useEffect(() => {
        if (!open) return;
        const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [open, onClose]);

    if (!open) return null;

    return (
        <div className="fixed inset-0 z-[200] flex justify-end" role="dialog" aria-modal="true">
            <div
                onClick={onClose}
                className="absolute inset-0 cursor-pointer"
                style={{ background: 'var(--vora-bg-overlay)' }}
                aria-hidden="true"
            />
            <aside
                className="relative flex h-full w-full max-w-md flex-col vora-glass animate-[slideIn_240ms_var(--vora-ease-out,ease-out)]"
                style={{ background: 'var(--vora-bg-surface)' }}
            >
                <header className="flex items-center justify-between px-5 py-4 border-b" style={{ borderColor: 'var(--vora-border-subtle)' }}>
                    <h2 className="m-0 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>{title}</h2>
                    <button
                        type="button"
                        onClick={onClose}
                        aria-label="Close"
                        className="inline-flex h-8 w-8 cursor-pointer items-center justify-center rounded-full transition-colors hover:bg-white/5"
                        style={{ color: 'var(--vora-text-muted)' }}
                    >
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                            <line x1="18" y1="6" x2="6" y2="18" />
                            <line x1="6" y1="6" x2="18" y2="18" />
                        </svg>
                    </button>
                </header>
                <div className="flex-1 overflow-y-auto">
                    {children}
                </div>
            </aside>
            <style>{`@keyframes slideIn { from { transform: translateX(100%); } to { transform: translateX(0); } }`}</style>
        </div>
    );
}
