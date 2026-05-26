interface FeatureTab {
    key: string;
    label: string;
}

interface FeatureTabsProps {
    tabs: FeatureTab[];
    activeKey: string;
    onChange: (key: string) => void;
    className?: string;
}

export default function FeatureTabs({ tabs, activeKey, onChange, className }: FeatureTabsProps) {
    return (
        <div className={`border-b border-[var(--vora-border-subtle)] mb-6 ${className ?? ''}`}>
            <div className="flex gap-1">
                {tabs.map(tab => {
                    const isActive = tab.key === activeKey;
                    return (
                        <button
                            key={tab.key}
                            type="button"
                            onClick={() => onChange(tab.key)}
                            className={`px-4 py-2 text-sm font-semibold transition-colors border-b-2 -mb-px cursor-pointer ${
                                isActive
                                    ? 'border-[var(--vora-accent-500)] text-[var(--vora-accent-text)]'
                                    : 'border-transparent text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)]'
                            }`}
                        >
                            {tab.label}
                        </button>
                    );
                })}
            </div>
        </div>
    );
}
