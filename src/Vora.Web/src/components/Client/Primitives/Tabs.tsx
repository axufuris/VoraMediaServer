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
    className?: string;
}

export default function Tabs<TKey extends string>({ tabs, active, onChange, className }: TabsProps<TKey>) {
    return (
        <nav className={`flex gap-7 border-b ${className ?? ''}`} style={{ borderColor: 'var(--vora-border-subtle)' }} role="tablist">
            {tabs.map(tab => {
                const isActive = tab.key === active;
                return (
                    <button
                        key={tab.key}
                        type="button"
                        role="tab"
                        aria-selected={isActive}
                        onClick={() => onChange(tab.key)}
                        className="relative pb-3.5 pt-2.5 cursor-pointer text-sm font-medium transition-colors"
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
                                className="absolute left-0 right-0 -bottom-px h-0.5 rounded-full"
                                style={{ background: 'var(--vora-accent-500)' }}
                            />
                        )}
                    </button>
                );
            })}
        </nav>
    );
}
