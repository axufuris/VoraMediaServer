import { useEffect, useState } from 'react';
import { featureFlagsService, type FeatureFlagsVM, DEFAULT_FEATURE_FLAGS } from '../../../api/System/featureFlagsService';

interface FeatureToggleProps {
    featureKey: keyof FeatureFlagsVM;
    label: string;
    description: string;
    serverId?: string;
    onChange?: (enabled: boolean) => void;
}

export default function FeatureToggle({ featureKey, label, description, serverId, onChange }: FeatureToggleProps) {
    const [flags, setFlags] = useState<FeatureFlagsVM>(DEFAULT_FEATURE_FLAGS);
    const [isLoading, setIsLoading] = useState(true);
    const [isSaving, setIsSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;
        featureFlagsService.getFeatureFlags(serverId)
            .then(data => {
                if (!cancelled) {
                    setFlags(data);
                    setIsLoading(false);
                }
            })
            .catch(err => {
                console.error('Failed to load feature flags', err);
                if (!cancelled) {
                    setError('Failed to load feature setting.');
                    setIsLoading(false);
                }
            });
        return () => { cancelled = true; };
    }, [serverId]);

    const value = flags[featureKey];

    const handleToggle = async () => {
        const next = !value;
        const updated: FeatureFlagsVM = { ...flags, [featureKey]: next };
        setFlags(updated);
        setIsSaving(true);
        setError(null);
        try {
            await featureFlagsService.updateFeatureFlags(updated, serverId);
            onChange?.(next);
        } catch (err) {
            console.error('Failed to update feature flag', err);
            setFlags(flags);
            setError('Failed to save. Try again.');
        } finally {
            setIsSaving(false);
        }
    };

    return (
        <div className="vora-card p-5 mb-6">
            <div className="flex items-start justify-between gap-6">
                <div className="flex-1 min-w-0">
                    <h2 className="text-base font-semibold text-[var(--vora-text-primary)] mb-0.5">{label}</h2>
                    <p className="text-sm text-[var(--vora-text-secondary)]">{description}</p>
                    {error && <p className="text-xs text-[var(--vora-danger-text)] mt-2">{error}</p>}
                </div>
                <button
                    type="button"
                    onClick={handleToggle}
                    disabled={isLoading || isSaving}
                    className={`relative inline-flex h-7 w-12 shrink-0 items-center rounded-full transition-colors cursor-pointer disabled:opacity-50 ${value ? 'bg-[var(--vora-accent-500)]' : 'bg-[var(--vora-border-strong)]'}`}
                    aria-pressed={value}
                >
                    <span className={`inline-block h-5 w-5 transform rounded-full bg-white transition-transform shadow-sm ${value ? 'translate-x-6' : 'translate-x-1'}`} />
                </button>
            </div>
        </div>
    );
}
