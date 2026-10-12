import type { ReactNode } from 'react';

export interface TabDefinition<TKey extends string> {
    key: TKey;
    label: string;
    icon?: ReactNode;
    badge?: ReactNode;
}

interface TabsProps<TKey extends string> {
    tabs: TabDefinition<TKey>[];
    active: TKey;
    onChange: (key: TKey) => void;
    // Page-level controls that belong beside the tabs rather than in the page
    // header — they sit flush right on the same line.
    actions?: ReactNode;
    className?: string;
}

export default function Tabs<TKey extends string>({ tabs, active, onChange, actions, className }: TabsProps<TKey>) {
    return (
        <nav className={`flex items-center gap-7 border-b ${className ?? ''}`} style={{ borderColor: 'var(--vora-border-subtle)' }} role="tablist">
            <div className="md:contents max-md:flex max-md:min-w-0 max-md:flex-1 max-md:gap-6 max-md:overflow-x-auto max-md:[scrollbar-width:none]">
                {tabs.map(tab => {
                    const isActive = tab.key === active;
                    return (
                        <button
                            key={tab.key}
                            type="button"
                            role="tab"
                            aria-selected={isActive}
                            onClick={() => onChange(tab.key)}
                            className="relative pb-3.5 pt-2.5 cursor-pointer text-sm font-medium transition-colors max-md:shrink-0 max-md:whitespace-nowrap"
                            style={{
                                color: isActive ? 'var(--vora-text-primary)' : 'var(--vora-text-muted)',
                                background: 'transparent',
                                border: 'none',
                            }}
                        >
                            <span className="inline-flex items-center gap-2">
                                {tab.icon}
                                {tab.label}
                                {tab.badge}
                            </span>
                            {isActive && (
                                <span
                                    className="absolute left-0 right-0 -bottom-px h-0.5 rounded-full max-md:bottom-0"
                                    style={{ background: 'var(--vora-accent-500)' }}
                                />
                            )}
                        </button>
                    );
                })}
            </div>
            {actions && <div className="ml-auto flex items-center gap-2 pb-2 pl-4 max-md:shrink-0 max-md:pl-2">{actions}</div>}
        </nav>
    );
}
