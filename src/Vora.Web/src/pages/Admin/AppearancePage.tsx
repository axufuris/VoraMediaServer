import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { themeService, type ThemeMetaVM } from '../../api/System/themeService';
import { useTheme } from '../../theme/useTheme';
import type { ThemeManifest } from '../../theme/types';
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
                without needing a real preview image. Built-in themes show the colours
                from their definitions; others get a neutral placeholder. */}
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

interface ThemeSwatch {
    canvas: string;
    surface: string;
    accent: string;
    text: string;
    muted: string;
}

const swatchOf = ({ tokens: { colors } }: ThemeManifest): ThemeSwatch => ({
    canvas: colors.bgCanvas,
    surface: colors.bgSurface,
    accent: colors.accent500,
    text: colors.textPrimary,
    muted: colors.textMuted,
});

const PLACEHOLDER_SWATCH: ThemeSwatch = {
    canvas: 'var(--vora-bg-sunken)',
    surface: 'var(--vora-bg-raised)',
    accent: 'var(--vora-border-strong)',
    text: 'var(--vora-text-muted)',
    muted: 'var(--vora-text-disabled)',
};

function ThemeSwatchStrip({ themeId }: { themeId: string }) {
    const { builtInThemes } = useTheme();
    const builtIn = builtInThemes.find(t => t.id === themeId);
    const swatches = builtIn ? swatchOf(builtIn) : PLACEHOLDER_SWATCH;
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

            <div className="px-8 max-md:px-4 pt-6 pb-10 max-w-6xl mx-auto">
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
