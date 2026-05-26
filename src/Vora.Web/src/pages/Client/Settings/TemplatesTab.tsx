import { useState, useEffect, useMemo } from 'react';
import { useParams } from 'react-router-dom';
import { clientTemplateService, type TemplateMetaVM } from '../../../api/System/clientTemplateService';
import { useClientTemplate } from '../../../theme/useClientTemplate';
import { useDialog } from '../../../dialogs';
import EmptyState from '../../../components/Client/Primitives/EmptyState';
import ScheduledTemplateBanner from '../../../components/Client/Primitives/ScheduledTemplateBanner';
import { StorageKeys } from '../../../utils/storageKeys';

interface TemplateSwatch {
    canvas: string;
    surface: string;
    accent: string;
    accentText: string;
    text: string;
}

const BUILT_IN_SWATCHES: Record<string, TemplateSwatch> = {
    'vora-cinema': { canvas: '#08080b', surface: '#101015', accent: '#f59e0b', accentText: '#fbbf24', text: '#fafafa' },
    'vora-noir':   { canvas: '#000000', surface: '#0a0a0a', accent: '#94a3b8', accentText: '#cbd5e1', text: '#f8fafc' },
    'vora-velvet': { canvas: '#1a0a0e', surface: '#2a1218', accent: '#c2410c', accentText: '#fed7aa', text: '#fef9c3' },
    'vora-aurora': { canvas: '#020617', surface: '#0c1726', accent: '#14b8a6', accentText: '#5eead4', text: '#e0f2fe' },
    default:       { canvas: '#0a0a0e', surface: '#181820', accent: '#9090a0', accentText: '#c4c4cc', text: '#fafafa' },
};

function TemplateSwatchStrip({ template, isActive }: { template: TemplateMetaVM, isActive: boolean }) {
    if (template.preview) {
        return (
            <div className="h-24 overflow-hidden" style={{ borderBottom: '1px solid var(--vora-border-subtle)' }}>
                <img src={template.preview} alt={`${template.name} preview`} className="h-full w-full object-cover" />
            </div>
        );
    }

    if (isActive) {
        return (
            <div className="grid h-24 grid-cols-5" style={{ borderBottom: '1px solid var(--vora-border-subtle)' }}>
                <div style={{ background: 'var(--vora-bg-canvas)' }} />
                <div style={{ background: 'var(--vora-bg-surface)' }} />
                <div style={{ background: 'var(--vora-accent-500)' }} />
                <div style={{ background: 'var(--vora-accent-text)' }} />
                <div style={{ background: 'var(--vora-text-primary)' }} />
            </div>
        );
    }

    const s = BUILT_IN_SWATCHES[template.id] ?? BUILT_IN_SWATCHES.default;
    return (
        <div className="grid h-24 grid-cols-5" style={{ borderBottom: '1px solid var(--vora-border-subtle)' }}>
            <div style={{ background: s.canvas }} />
            <div style={{ background: s.surface }} />
            <div style={{ background: s.accent }} />
            <div style={{ background: s.accentText }} />
            <div style={{ background: s.text }} />
        </div>
    );
}

const SPOTLIGHT_PREF_KEY = (profileId: string) => StorageKeys.spotlight(profileId);

export default function TemplatesTab({ activeProfileId }: { activeProfileId: string }) {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const { active, activeInfo, activeSchedule, isSwitching, setActive, clearActive, refresh } = useClientTemplate();
    const [templates, setTemplates] = useState<TemplateMetaVM[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [savingId, setSavingId] = useState<string | null>(null);
    const [showSpotlight, setShowSpotlight] = useState<boolean>(() => {
        if (!activeProfileId) return true;
        const stored = localStorage.getItem(SPOTLIGHT_PREF_KEY(activeProfileId));
        return stored === null ? true : stored === 'true';
    });

    useEffect(() => {
        if (!activeProfileId) return;
        const stored = localStorage.getItem(SPOTLIGHT_PREF_KEY(activeProfileId));
        queueMicrotask(() => setShowSpotlight(stored === null ? true : stored === 'true'));
    }, [activeProfileId]);

    const handleSpotlightToggle = (next: boolean) => {
        setShowSpotlight(next);
        if (activeProfileId) {
            localStorage.setItem(SPOTLIGHT_PREF_KEY(activeProfileId), String(next));
            window.dispatchEvent(new CustomEvent('vora:home-prefs-changed'));
        }
    };

    useEffect(() => {
        let cancelled = false;
        clientTemplateService.getAll(serverId)
            .then(list => { if (!cancelled) setTemplates(list); })
            .catch(err => console.error('Failed to load templates', err))
            .finally(() => { if (!cancelled) setIsLoading(false); });
        return () => { cancelled = true; };
    }, [serverId]);

    const handleSelect = async (templateId: string) => {
        setSavingId(templateId);
        try {
            const ok = await setActive(templateId);
            if (!ok) {
                await dialog.alert({ title: 'Could not apply template', message: 'Please try again.', tone: 'danger' });
            }
        } finally {
            setSavingId(null);
        }
    };

    const handleRevert = async () => {
        const ok = await clearActive();
        if (!ok) {
            await dialog.alert({ title: 'Could not revert', message: 'Please try again.', tone: 'danger' });
            return;
        }
        await refresh();
    };

    const handleUseScheduled = async () => {
        if (!activeSchedule) return;
        await handleSelect(activeSchedule.templateId);
    };

    const sortedTemplates = useMemo(() => {
        return [...templates].sort((a, b) => {
            if (a.isBuiltIn !== b.isBuiltIn) return a.isBuiltIn ? -1 : 1;
            return a.name.localeCompare(b.name);
        });
    }, [templates]);

    const source = activeInfo?.source;
    const activeId = active.id;

    return (
        <div>
            {activeSchedule && (
                <div className="mb-6">
                    <ScheduledTemplateBanner
                        schedule={activeSchedule}
                        isOverridden={source === 'Override'}
                        onRevert={source === 'Override' || source === 'Profile' ? handleRevert : undefined}
                        onApplySchedule={source === 'Override' ? handleUseScheduled : undefined}
                    />
                </div>
            )}

            {!activeSchedule && source === 'Profile' && (
                <div
                    className="mb-6 flex items-center justify-between gap-4 rounded-xl p-4"
                    style={{
                        background: 'var(--vora-bg-surface)',
                        border: '1px solid var(--vora-border-subtle)',
                    }}
                >
                    <div className="flex items-start gap-3">
                        <div
                            className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg"
                            style={{ background: 'var(--vora-accent-soft)', color: 'var(--vora-accent-text)' }}
                        >
                            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75">
                                <circle cx="12" cy="12" r="10" />
                                <line x1="12" y1="16" x2="12" y2="12" />
                                <line x1="12" y1="8" x2="12.01" y2="8" />
                            </svg>
                        </div>
                        <div>
                            <p className="m-0 text-sm font-semibold" style={{ color: 'var(--vora-text-primary)' }}>You've picked a template for this profile</p>
                            <p className="m-0 mt-0.5 text-xs" style={{ color: 'var(--vora-text-muted)' }}>
                                Your pick wins over whatever the server admin sets as the default. Match the default to follow whatever the admin chooses going forward.
                            </p>
                        </div>
                    </div>
                    <button type="button" onClick={handleRevert} className="vora-button-secondary shrink-0 cursor-pointer">
                        Match server default
                    </button>
                </div>
            )}

            <section className="vora-card mb-6 p-5">
                <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Home page</h2>
                <p className="m-0 mb-4 text-sm" style={{ color: 'var(--vora-text-muted)' }}>Tune what shows up the moment you land on Home.</p>
                <label className="flex cursor-pointer items-start justify-between gap-4 rounded-lg p-3 transition-colors hover:bg-[var(--vora-bg-sunken)]/40">
                    <div>
                        <p className="m-0 text-sm font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Show spotlight hero</p>
                        <p className="m-0 mt-0.5 text-xs" style={{ color: 'var(--vora-text-muted)' }}>
                            The big rotating banner at the top of Home. Turn off if you'd rather jump straight to your rails.
                        </p>
                    </div>
                    <span
                        className="relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors"
                        style={{ background: showSpotlight ? 'var(--vora-accent-500)' : 'var(--vora-bg-raised)', border: '1px solid var(--vora-border-subtle)' }}
                    >
                        <input
                            type="checkbox"
                            checked={showSpotlight}
                            onChange={e => handleSpotlightToggle(e.target.checked)}
                            className="absolute inset-0 cursor-pointer opacity-0"
                            aria-label="Show spotlight hero on Home"
                        />
                        <span
                            className="inline-block h-4 w-4 transform rounded-full shadow-sm transition-transform"
                            style={{ background: 'var(--vora-text-primary)', transform: `translateX(${showSpotlight ? '22px' : '4px'})` }}
                        />
                    </span>
                </label>
            </section>

            {isLoading ? (
                <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
                    {[1, 2, 3, 4, 5, 6].map(i => <div key={i} className="vora-skeleton h-64" />)}
                </div>
            ) : sortedTemplates.length === 0 ? (
                <EmptyState
                    title="No templates installed"
                    description="Drop a template bundle into the server's Templates folder and ask an admin to rescan."
                />
            ) : (
                <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
                    {sortedTemplates.map(template => {
                        const isActive = activeId === template.id;
                        const isSaving = savingId === template.id || (isSwitching && isActive);
                        const buttonLabel = activeSchedule
                            ? (isActive
                                ? 'In use'
                                : source === 'Override' && activeSchedule.templateId === template.id
                                    ? 'Use scheduled'
                                    : `Override "${activeSchedule.name}"`)
                            : (isActive ? 'In use' : 'Set as default');

                        return (
                            <div
                                key={template.id}
                                className="vora-card overflow-hidden"
                                style={isActive ? { boxShadow: '0 0 0 2px var(--vora-accent-500)' } : undefined}
                            >
                                <TemplateSwatchStrip template={template} isActive={isActive} />
                                <div className="flex flex-1 flex-col p-5">
                                    <div className="mb-1 flex items-start justify-between gap-3">
                                        <h3 className="m-0 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>{template.name}</h3>
                                        <div className="flex shrink-0 flex-wrap gap-1.5">
                                            {template.isBuiltIn ? (
                                                <span className="rounded px-2 py-0.5 text-[10px] font-semibold" style={{ background: 'rgba(255,255,255,0.06)', color: 'var(--vora-text-secondary)', border: '1px solid var(--vora-border-subtle)' }}>Built-in</span>
                                            ) : (
                                                <span className="rounded px-2 py-0.5 text-[10px] font-semibold" style={{ background: 'var(--vora-info-soft)', color: 'var(--vora-info-text)', border: '1px solid color-mix(in srgb, var(--vora-info-500) 35%, transparent)' }}>Plugin</span>
                                            )}
                                            {isActive && (
                                                <span className="rounded px-2 py-0.5 text-[10px] font-semibold" style={{ background: 'var(--vora-success-soft)', color: 'var(--vora-success-text)', border: '1px solid color-mix(in srgb, var(--vora-success-500) 35%, transparent)' }}>Active</span>
                                            )}
                                        </div>
                                    </div>
                                    {template.description && (
                                        <p className="m-0 text-sm" style={{ color: 'var(--vora-text-muted)' }}>{template.description}</p>
                                    )}
                                    <div className="mt-3 flex items-center gap-2 text-xs" style={{ color: 'var(--vora-text-disabled)' }}>
                                        {template.author && <span>by {template.author}</span>}
                                        {template.author && <span>·</span>}
                                        <span>v{template.version}</span>
                                    </div>
                                    <div className="mt-4 flex justify-end border-t pt-3" style={{ borderColor: 'var(--vora-border-subtle)' }}>
                                        {isActive ? (
                                            <span className="text-xs font-semibold" style={{ color: 'var(--vora-text-muted)' }}>{buttonLabel}</span>
                                        ) : (
                                            <button
                                                type="button"
                                                onClick={() => handleSelect(template.id)}
                                                disabled={isSaving}
                                                className="vora-button-primary cursor-pointer text-xs disabled:opacity-50"
                                            >
                                                {isSaving ? 'Applying…' : buttonLabel}
                                            </button>
                                        )}
                                    </div>
                                </div>
                            </div>
                        );
                    })}
                </div>
            )}
        </div>
    );
}
