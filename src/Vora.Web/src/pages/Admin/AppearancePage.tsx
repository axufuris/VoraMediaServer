import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { themeService, type ThemeMetaVM } from '../../api/System/themeService';
import { useTheme } from '../../theme/useTheme';
import { useDialog } from '../../dialogs';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import HealthBadge from '../../components/Admin/Primitives/HealthBadge';
import EmptyState from '../../components/Admin/Primitives/EmptyState';

interface ThemeCardProps {
    meta: ThemeMetaVM;
    isActive: boolean;
    isSaving: boolean;
    onSelect: () => void;
}

function ThemeCard({ meta, isActive, isSaving, onSelect }: ThemeCardProps) {
    return (
        <div
            className={`vora-card overflow-hidden flex flex-col transition-all ${isActive ? 'ring-2 ring-[var(--vora-accent-500)]' : 'vora-card-interactive cursor-pointer'}`}
            onClick={!isActive && !isSaving ? onSelect : undefined}
            role={!isActive ? 'button' : undefined}
            tabIndex={!isActive ? 0 : undefined}
            onKeyDown={!isActive ? (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onSelect(); } } : undefined}
        >
            {/* Swatch strip — sits above the card to communicate the theme's palette
                without needing a real preview image. Pulls live tokens from :root if
                this is the active card; falls back to neutral swatches otherwise. */}
            <div className="h-20 relative overflow-hidden border-b border-[var(--vora-border-subtle)]">
                {meta.preview ? (
                    <img src={meta.preview} alt={`${meta.name} preview`} className="w-full h-full object-cover" />
                ) : (
                    <ThemeSwatchStrip themeId={meta.id} />
                )}
            </div>

            <div className="p-5 flex-1 flex flex-col">
                <div className="flex items-start justify-between gap-3 mb-1">
                    <h3 className="text-base font-semibold text-[var(--vora-text-primary)]">{meta.name}</h3>
                    <div className="flex flex-wrap gap-1.5 shrink-0">
                        {meta.isBuiltIn
                            ? <HealthBadge tone="neutral" showDot={false}>Built-in</HealthBadge>
                            : <HealthBadge tone="info" showDot={false}>Plugin</HealthBadge>}
                        {isActive && <HealthBadge tone="ok">Active</HealthBadge>}
                    </div>
                </div>
                {meta.description && <p className="text-sm text-[var(--vora-text-muted)]">{meta.description}</p>}
                <div className="mt-3 flex items-center gap-3 text-xs text-[var(--vora-text-disabled)]">
                    {meta.author && <span>by {meta.author}</span>}
                    {meta.author && <span>·</span>}
                    <span>v{meta.version}</span>
                </div>

                <div className="mt-4 pt-3 border-t border-[var(--vora-border-subtle)] flex justify-end">
                    {isActive ? (
                        <span className="text-xs font-semibold text-[var(--vora-text-muted)]">In use</span>
                    ) : (
                        <button
                            type="button"
                            onClick={(e) => { e.stopPropagation(); onSelect(); }}
                            disabled={isSaving}
                            className="vora-button-primary text-xs disabled:opacity-50"
                        >
                            {isSaving ? 'Applying…' : 'Set active'}
                        </button>
                    )}
                </div>
            </div>
        </div>
    );
}

/**
 * Mini palette strip. The active theme's swatches use the live CSS variables so
 * the strip changes color along with the theme. Other themes show their key
 * colors via inline styles read from the bundled manifests (best-effort —
 * if the theme isn't a known built-in, we render a neutral placeholder).
 */
function ThemeSwatchStrip({ themeId }: { themeId: string }) {
    // We could import the manifests directly to read their token colors, but
    // that creates a circular dep (manifests → registry → page → manifests).
    // Instead: the active theme uses live CSS vars (always accurate); inactive
    // themes get an inline palette keyed by known IDs.
    const swatches = INACTIVE_SWATCHES[themeId] ?? INACTIVE_SWATCHES.default;
    return (
        <div className="absolute inset-0 grid grid-cols-5 gap-0">
            <div style={{ background: swatches.canvas }} />
            <div style={{ background: swatches.surface }} />
            <div style={{ background: swatches.accent }} />
            <div style={{ background: swatches.text }} />
            <div style={{ background: swatches.muted }} />
        </div>
    );
}

/**
 * Tiny palette hints used by ThemeSwatchStrip for built-in themes. Kept here
 * rather than re-importing the manifests to avoid the circular dependency.
 * Plugin themes that don't appear in this map render the default palette.
 */
const INACTIVE_SWATCHES: Record<string, { canvas: string, surface: string, accent: string, text: string, muted: string }> = {
    'vora-light':   { canvas: '#fafaf9', surface: '#ffffff', accent: '#d97706', text: '#1c1917', muted: '#78716c' },
    'vora-dark':    { canvas: '#09090b', surface: '#18181b', accent: '#f59e0b', text: '#fafafa', muted: '#71717a' },
    'vora-ocean':   { canvas: '#0a1623', surface: '#0f1f30', accent: '#14b8a6', text: '#e2e8f0', muted: '#64748b' },
    default:        { canvas: '#e7e5e4', surface: '#f5f5f4', accent: '#a3a3a3', text: '#525252', muted: '#a3a3a3' },
};

export default function AppearancePage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const dialog = useDialog();
    const { active, setActive } = useTheme();
    const [themes, setThemes] = useState<ThemeMetaVM[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [savingId, setSavingId] = useState<string | null>(null);
    const [isRescanning, setIsRescanning] = useState(false);

    const loadThemes = async () => {
        try {
            const list = await themeService.getAll(serverId);
            setThemes(list);
        } catch (err) {
            console.error('Failed to load themes', err);
            setThemes([]);
        }
    };

    useEffect(() => {
        let cancelled = false;
        themeService.getAll(serverId)
            .then(list => { if (!cancelled) setThemes(list); })
            .catch(err => {
                console.error('Failed to load themes', err);
                if (!cancelled) setThemes([]);
            })
            .finally(() => { if (!cancelled) setIsLoading(false); });
        return () => { cancelled = true; };
    }, [serverId]);

    const handleSelect = async (themeId: string) => {
        setSavingId(themeId);
        const ok = await setActive(themeId);
        setSavingId(null);
        if (!ok) {
            await dialog.alert('Failed to apply theme. Please try again.');
        }
    };

    const handleRescan = async () => {
        setIsRescanning(true);
        try {
            const bundleCount = await themeService.rescan(serverId);
            await loadThemes();
            await dialog.alert(`Re-scanned the Themes folder. ${bundleCount} plugin theme${bundleCount === 1 ? '' : 's'} loaded.`);
        } catch (err) {
            console.error('Rescan failed', err);
            await dialog.alert('Failed to re-scan themes. Check the API console for details.');
        } finally {
            setIsRescanning(false);
        }
    };

    return (
        <div data-vora-page="">
            <PageHeader
                title="Appearance"
                description="Pick the theme that paints this server's admin surface. The choice applies to every admin signed in here."
                actions={
                    <button
                        type="button"
                        onClick={handleRescan}
                        disabled={isRescanning}
                        className="vora-button-secondary flex items-center gap-2 disabled:opacity-50"
                        title="Re-scan the Themes/ folder on disk for new or changed bundles"
                    >
                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" /></svg>
                        {isRescanning ? 'Re-scanning…' : 'Re-scan bundles'}
                    </button>
                }
            />

            <div className="px-8 pt-6 pb-10 max-w-6xl mx-auto">
                {isLoading ? (
                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                        {[1, 2, 3].map(i => <div key={i} className="vora-skeleton h-56" />)}
                    </div>
                ) : themes.length === 0 ? (
                    <div className="vora-card">
                        <EmptyState
                            title="No themes available"
                            description="The server didn't return any themes. This is unexpected — the built-in Vora Default theme should always be present."
                        />
                    </div>
                ) : (
                    <>
                        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                            {themes.map(meta => (
                                <ThemeCard
                                    key={meta.id}
                                    meta={meta}
                                    isActive={meta.id === active.id}
                                    isSaving={savingId === meta.id}
                                    onSelect={() => handleSelect(meta.id)}
                                />
                            ))}
                        </div>

                        <p className="mt-6 text-xs text-[var(--vora-text-muted)]">
                            Plugin themes live in <code className="font-mono text-[var(--vora-text-secondary)]">Themes/&lt;theme-id&gt;/</code> on the server.
                            Each bundle is a folder with a <code className="font-mono text-[var(--vora-text-secondary)]">manifest.json</code> and an
                            optional <code className="font-mono text-[var(--vora-text-secondary)]">assets/</code> directory.
                            See <code className="font-mono text-[var(--vora-text-secondary)]">docs/admin-theme-bundles.md</code> for the format.
                        </p>
                    </>
                )}
            </div>
        </div>
    );
}
