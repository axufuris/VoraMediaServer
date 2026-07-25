import { useEffect, useState, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import {
    adminService,
    type DedupeGroupVM,
    type DedupeSettingsVM,
    type DedupeIgnoredGroupVM
} from '../../api/System/adminService';
import { libraryService, type LibrarySummary } from '../../api/Media/libraryService';
import { useDialog } from '../../dialogs';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import HealthBadge from '../../components/Admin/Primitives/HealthBadge';
import EmptyState from '../../components/Admin/Primitives/EmptyState';

type TabId = 'duplicates' | 'rules' | 'ignored';

function formatBytes(bytes: number) {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
}

function audioChannelCount(track: string) {
    const match = track.match(/(\d+)\s*CH/i);
    return match ? parseInt(match[1], 10) : 0;
}

function sortAudioTracksByChannels(tracks: string[]) {
    return [...tracks].sort((a, b) => audioChannelCount(a) - audioChannelCount(b));
}

function Chip({ children, tone = 'neutral' }: { children: React.ReactNode, tone?: 'neutral' | 'highlight' }) {
    const cls = tone === 'highlight'
        ? 'bg-[var(--vora-accent-soft)] text-[var(--vora-accent-text)]'
        : 'bg-[var(--vora-bg-sunken)] text-[var(--vora-text-secondary)]';
    return <span className={`text-[11px] font-semibold px-2 py-0.5 rounded ${cls}`}>{children}</span>;
}

export default function DedupePage() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const [activeTab, setActiveTab] = useState<TabId>('duplicates');

    return (
        <div data-vora-page="">
            <PageHeader
                title="Media Deduplication"
                description="Identify and remove duplicate files, tune the rules used to detect them, and review groups you've chosen to ignore."
            />

            <div className="px-8 pt-2 max-w-7xl mx-auto">
                <div className="flex gap-2 border-b border-[var(--vora-border-subtle)] mb-2">
                    <TabButton id="duplicates" activeTab={activeTab} setActiveTab={setActiveTab}>Duplicates</TabButton>
                    <TabButton id="rules" activeTab={activeTab} setActiveTab={setActiveTab}>Rules</TabButton>
                    <TabButton id="ignored" activeTab={activeTab} setActiveTab={setActiveTab}>Ignored</TabButton>
                </div>
            </div>

            <div className="px-8 pb-10 max-w-7xl mx-auto pt-6">
                {activeTab === 'duplicates' && <DuplicatesTab dialog={dialog} serverId={serverId} />}
                {activeTab === 'rules' && <RulesTab dialog={dialog} serverId={serverId} />}
                {activeTab === 'ignored' && <IgnoredTab dialog={dialog} serverId={serverId} />}
            </div>
        </div>
    );
}

function TabButton({ id, activeTab, setActiveTab, children }: { id: TabId, activeTab: TabId, setActiveTab: (t: TabId) => void, children: React.ReactNode }) {
    const active = activeTab === id;
    return (
        <button
            type="button"
            onClick={() => setActiveTab(id)}
            className={`px-4 py-2.5 text-sm font-semibold border-b-2 -mb-px transition-colors cursor-pointer ${
                active
                    ? 'border-[var(--vora-accent-500)] text-[var(--vora-text-primary)]'
                    : 'border-transparent text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)]'
            }`}
        >
            {children}
        </button>
    );
}

type DialogApi = ReturnType<typeof useDialog>;

function DuplicatesTab({ dialog, serverId }: { dialog: DialogApi, serverId?: string }) {
    const [groups, setGroups] = useState<DedupeGroupVM[]>([]);
    const [loading, setLoading] = useState(true);

    const fetchDuplicates = useCallback(async () => {
        setLoading(true);
        try {
            const data = await adminService.getDuplicates(serverId);
            setGroups(data);
        } catch (error) {
            console.error('Failed to fetch duplicates', error);
        } finally {
            setLoading(false);
        }
    }, [serverId]);

    useEffect(() => {
        fetchDuplicates();
    }, [fetchDuplicates]);

    const handleDelete = async (groupId: string, partId: string, deletePhysical: boolean) => {
        const actionText = deletePhysical ? 'delete this file from the disk AND database' : 'remove this file from the database ONLY';
        if (!await dialog.confirm(`Are you sure you want to ${actionText}?`)) return;

        try {
            await adminService.deleteDuplicate(partId, deletePhysical, serverId);
            setGroups(prev => {
                const updated = prev.map(g => {
                    if (g.mediaItemId === groupId) {
                        return { ...g, parts: g.parts.filter(p => p.partId !== partId) };
                    }
                    return g;
                });
                return updated.filter(g => g.parts.length > 1);
            });
        } catch (error: unknown) {
            const err = error as { response?: { data?: { message?: string } } };
            await dialog.alert(err.response?.data?.message || 'Failed to delete duplicate.');
        }
    };

    const handleIgnore = async (group: DedupeGroupVM) => {
        const note = await dialog.prompt({
            message: `Ignore this duplicate group?\n\n"${group.title}" (${group.resolution}) will no longer appear on the duplicates list. You can restore it later from the Ignored tab.`,
            placeholder: 'Optional note (e.g. "kept both editions on purpose")',
            confirmText: 'Ignore',
            cancelText: 'Cancel'
        });
        if (note === null) return;

        try {
            await adminService.ignoreDuplicateGroup(group.mediaItemId, group.resolution, note || undefined, serverId);
            setGroups(prev => prev.filter(g => !(g.mediaItemId === group.mediaItemId && g.resolution === group.resolution)));
        } catch (error: unknown) {
            const err = error as { response?: { data?: { message?: string } } };
            await dialog.alert(err.response?.data?.message || 'Failed to ignore group.');
        }
    };

    const potentialSavings = groups.reduce(
        (sum, g) => sum + g.parts.slice(1).reduce((s, p) => s + p.fileSizeBytes, 0),
        0
    );

    return (
        <>
            <div className="flex justify-between items-center gap-4 mb-4 flex-wrap">
                {groups.length > 0 ? (
                    <div className="vora-card px-5 py-3 flex items-baseline gap-2">
                        <span className="text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">Potential savings</span>
                        <span className="text-xl font-bold text-[var(--vora-accent-text)]">{formatBytes(potentialSavings)}</span>
                        <span className="text-xs text-[var(--vora-text-muted)]">if you keep the best version in each group</span>
                    </div>
                ) : <div />}
                <button type="button" onClick={fetchDuplicates} className="vora-button-secondary flex items-center gap-2">
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" /></svg>
                    Rescan
                </button>
            </div>

            {loading ? (
                <div className="vora-skeleton h-48" />
            ) : groups.length === 0 ? (
                <div className="vora-card">
                    <EmptyState
                        title="No duplicates found"
                        description="Your library matches your current rules — nothing to surface."
                        icon={<svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M5 13l4 4L19 7" /></svg>}
                    />
                </div>
            ) : (
                <div className="space-y-6">
                    {groups.map(group => (
                        <section key={`${group.mediaItemId}|${group.resolution}`} className="vora-card overflow-hidden">
                            <div className="bg-[var(--vora-bg-sunken)] px-5 py-4 border-b border-[var(--vora-border-subtle)] flex justify-between items-center gap-3 flex-wrap">
                                <div>
                                    <h3 className="text-base font-semibold text-[var(--vora-text-primary)]">{group.title}</h3>
                                    <div className="flex gap-1.5 mt-1.5">
                                        <Chip tone="highlight">{group.type}</Chip>
                                        {group.mediaKind === 'video' && <Chip tone="highlight">{group.resolution}</Chip>}
                                    </div>
                                </div>
                                <div className="flex gap-2 items-center">
                                    <HealthBadge tone="warn">{group.parts.length} versions</HealthBadge>
                                    <button
                                        type="button"
                                        onClick={() => handleIgnore(group)}
                                        className="text-xs font-semibold px-3 py-1.5 bg-[var(--vora-bg-canvas)] hover:bg-[var(--vora-border-subtle)] text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] rounded-[var(--vora-radius-md)] transition-colors cursor-pointer border border-[var(--vora-border-subtle)]"
                                        title="Hide this group from the duplicates list"
                                    >
                                        Ignore group
                                    </button>
                                </div>
                            </div>

                            <div className="p-4 space-y-3">
                                {group.parts.map((part, index) => (
                                    <div
                                        key={part.partId}
                                        className={`p-4 rounded-[var(--vora-radius-md)] border flex flex-col md:flex-row gap-4 justify-between items-start md:items-center ${
                                            index === 0
                                                ? 'bg-[var(--vora-info-soft)] border-[var(--vora-info-500)]/30'
                                                : 'bg-[var(--vora-bg-canvas)] border-[var(--vora-border-subtle)]'
                                        }`}
                                    >
                                        <div className="flex-1 min-w-0">
                                            <div className="flex items-center gap-2 mb-2">
                                                {index === 0 && <HealthBadge tone="info" showDot={false}>Best</HealthBadge>}
                                                <p className="font-mono text-sm text-[var(--vora-text-primary)] break-all">{part.fileName}</p>
                                            </div>
                                            <div className="flex flex-wrap gap-1.5">
                                                <Chip>Score: <span className="text-[var(--vora-text-primary)] font-bold ml-0.5">{part.qualityScore}</span></Chip>
                                                <Chip>{formatBytes(part.fileSizeBytes)}</Chip>
                                                {group.mediaKind === 'audio' ? (
                                                    <>
                                                        {part.bitrate && <Chip>{Math.round(part.bitrate / 1000)} kbps</Chip>}
                                                        <Chip>{part.container}</Chip>
                                                        {part.audioCodec && <Chip>{part.audioCodec}</Chip>}
                                                        {part.sampleRate && <Chip>{(part.sampleRate / 1000).toFixed(part.sampleRate % 1000 === 0 ? 0 : 1)} kHz</Chip>}
                                                    </>
                                                ) : (
                                                    <>
                                                        {part.bitrate && <Chip>{Math.round(part.bitrate / 1000000)} Mbps</Chip>}
                                                        <Chip>{part.container}</Chip>
                                                        {part.source && <Chip tone="highlight">{part.source}</Chip>}
                                                        {part.videoCodec && <Chip>{part.videoCodec}</Chip>}
                                                        {part.hdrFormat !== 'SDR' ? <Chip tone="highlight">{part.hdrFormat}</Chip> : <Chip>SDR</Chip>}
                                                    </>
                                                )}
                                            </div>
                                            {group.mediaKind === 'video' && part.audioTracks.length > 0 && (
                                                <div className="mt-2 flex flex-wrap gap-1">
                                                    {sortAudioTracksByChannels(part.audioTracks).map((audio, i) => (
                                                        <span key={i} className="text-[10px] uppercase tracking-wider bg-[var(--vora-bg-sunken)] text-[var(--vora-text-muted)] px-2 py-0.5 rounded border border-[var(--vora-border-subtle)] font-semibold">{audio}</span>
                                                    ))}
                                                </div>
                                            )}
                                        </div>

                                        <div className="flex flex-col gap-2 shrink-0 w-full md:w-auto">
                                            <button
                                                type="button"
                                                onClick={() => handleDelete(group.mediaItemId, part.partId, true)}
                                                className="text-xs font-semibold px-3 py-1.5 bg-[var(--vora-danger-soft)] hover:bg-[var(--vora-danger-500)] text-[var(--vora-danger-text)] hover:text-[var(--vora-text-primary)] rounded-[var(--vora-radius-md)] transition-colors cursor-pointer"
                                            >
                                                Delete file & DB
                                            </button>
                                            <button
                                                type="button"
                                                onClick={() => handleDelete(group.mediaItemId, part.partId, false)}
                                                className="text-xs font-semibold px-3 py-1.5 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-border-strong)] text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] rounded-[var(--vora-radius-md)] transition-colors cursor-pointer"
                                            >
                                                Remove from DB only
                                            </button>
                                        </div>
                                    </div>
                                ))}
                            </div>
                        </section>
                    ))}
                </div>
            )}
        </>
    );
}

function RulesTab({ dialog, serverId }: { dialog: DialogApi, serverId?: string }) {
    const [defaults, setDefaults] = useState<DedupeSettingsVM | null>(null);
    const [global, setGlobal] = useState<DedupeSettingsVM | null>(null);
    const [libraries, setLibraries] = useState<LibrarySummary[]>([]);
    const [overrides, setOverrides] = useState<Record<string, DedupeSettingsVM>>({});
    const [selectedLibraryId, setSelectedLibraryId] = useState<string | 'global'>('global');
    const [saving, setSaving] = useState(false);
    const [loading, setLoading] = useState(true);

    const load = useCallback(async () => {
        setLoading(true);
        try {
            const [g, d, libs, ovs] = await Promise.all([
                adminService.getDedupeSettings(serverId),
                adminService.getDedupeDefaults(serverId),
                libraryService.getLibraries(serverId),
                adminService.getLibraryDedupeOverrides(serverId)
            ]);
            setGlobal(g);
            setDefaults(d);
            setLibraries(libs);
            const map: Record<string, DedupeSettingsVM> = {};
            for (const o of ovs) {
                if (o.libraryId) map[o.libraryId] = o;
            }
            setOverrides(map);
        } catch (err) {
            console.error('Failed to load dedupe settings', err);
            await dialog.alert('Failed to load dedupe settings.');
        } finally {
            setLoading(false);
        }
    }, [serverId, dialog]);

    useEffect(() => { load(); }, [load]);

    const editing: DedupeSettingsVM | null = (() => {
        if (selectedLibraryId === 'global') return global;
        return overrides[selectedLibraryId] || (global ? { ...global, libraryId: selectedLibraryId, isDefault: true } : null);
    })();

    const setEditing = (next: DedupeSettingsVM) => {
        if (selectedLibraryId === 'global') {
            setGlobal({ ...next, libraryId: null, isDefault: false });
        } else {
            setOverrides(prev => ({ ...prev, [selectedLibraryId]: { ...next, libraryId: selectedLibraryId, isDefault: false } }));
        }
    };

    const handleSave = async () => {
        if (!editing) return;
        setSaving(true);
        try {
            if (selectedLibraryId === 'global') {
                const saved = await adminService.updateDedupeSettings(editing, serverId);
                setGlobal(saved);
            } else {
                const saved = await adminService.updateLibraryDedupeSettings(selectedLibraryId, editing, serverId);
                setOverrides(prev => ({ ...prev, [selectedLibraryId]: saved }));
            }
            await dialog.alert('Rules saved.');
        } catch (err) {
            console.error(err);
            await dialog.alert('Failed to save rules.');
        } finally {
            setSaving(false);
        }
    };

    const handleResetToDefaults = async () => {
        if (!defaults) return;
        if (!await dialog.confirm('Reset all values back to defaults? This will overwrite your current edits (but does not save until you click Save).')) return;
        setEditing({ ...defaults, libraryId: selectedLibraryId === 'global' ? null : selectedLibraryId, isDefault: false });
    };

    const handleClearOverride = async () => {
        if (selectedLibraryId === 'global') return;
        if (!await dialog.confirm('Remove the per-library override and fall back to the global rules?')) return;
        try {
            await adminService.clearLibraryDedupeSettings(selectedLibraryId, serverId);
            setOverrides(prev => {
                const next = { ...prev };
                delete next[selectedLibraryId];
                return next;
            });
        } catch (err) {
            console.error(err);
            await dialog.alert('Failed to clear override.');
        }
    };

    if (loading || !editing || !defaults) {
        return <div className="vora-skeleton h-96" />;
    }

    const isOverride = selectedLibraryId !== 'global' && !!overrides[selectedLibraryId];
    const selectedLibrary = selectedLibraryId === 'global' ? null : libraries.find(l => l.id === selectedLibraryId);
    const selectedType = selectedLibrary?.type?.toLowerCase() ?? '';
    const isGlobalScope = selectedLibraryId === 'global';
    const isMusicScope = selectedType === 'music';
    const showVideoSections = isGlobalScope || !isMusicScope;
    const showAudioSections = isGlobalScope || isMusicScope;

    return (
        <div className="space-y-6">
            <div className="vora-card p-5 flex flex-wrap gap-4 items-end">
                <div className="flex-1 min-w-[240px]">
                    <FieldLabel>Apply to</FieldLabel>
                    <select
                        value={selectedLibraryId}
                        onChange={e => setSelectedLibraryId(e.target.value)}
                        className="vora-input cursor-pointer"
                    >
                        <option value="global">Global (all libraries unless overridden)</option>
                        {libraries.map(lib => (
                            <option key={lib.id} value={lib.id}>
                                {lib.name} {overrides[lib.id] ? '— override active' : ''}
                            </option>
                        ))}
                    </select>
                </div>
                <div className="flex gap-2">
                    <button type="button" onClick={handleResetToDefaults} className="vora-button-secondary">
                        Reset to defaults
                    </button>
                    {isOverride && (
                        <button type="button" onClick={handleClearOverride} className="vora-button-secondary text-[var(--vora-danger-text)]">
                            Remove override
                        </button>
                    )}
                    <button type="button" onClick={handleSave} disabled={saving} className="vora-button-primary">
                        {saving ? 'Saving…' : 'Save rules'}
                    </button>
                </div>
            </div>

            {selectedLibraryId !== 'global' && !isOverride && (
                <div className="text-xs text-[var(--vora-text-muted)] bg-[var(--vora-info-soft)] border border-[var(--vora-info-500)]/30 rounded-[var(--vora-radius-md)] px-3 py-2">
                    This library is using the global rules. Edit and save to create an override.
                </div>
            )}

            <SettingsSection title="Eligibility thresholds" description="Files that fall below these are never flagged.">
                <NumberField
                    label="Minimum file size (MB)"
                    value={Math.round(editing.minimumFileSizeBytes / (1024 * 1024))}
                    onChange={v => setEditing({ ...editing, minimumFileSizeBytes: v * 1024 * 1024 })}
                    hint="Files smaller than this are skipped (avoid flagging sample files)."
                />
                {showVideoSections && (
                    <NumberField
                        label="Minimum runtime (seconds)"
                        value={editing.minimumRuntimeSeconds}
                        onChange={v => setEditing({ ...editing, minimumRuntimeSeconds: v })}
                        hint="Files shorter than this are skipped (avoid flagging extras and trailers). Video only."
                    />
                )}
            </SettingsSection>

            {showVideoSections && (
                <>
                    {isGlobalScope && <SectionHeader>Video rules — Movies, TV Shows, Home Video</SectionHeader>}

                    <SettingsSection title="Video matching criteria" description="Controls when video files are considered duplicates of each other.">
                        <Checkbox
                            checked={editing.groupAcrossResolutions}
                            onChange={v => setEditing({ ...editing, groupAcrossResolutions: v })}
                            label="Treat different resolutions as duplicates of the same title"
                            hint="Off (default): only files with the same normalized resolution are grouped. On: every file for the same title becomes one group."
                        />
                        <NumberField
                            label="Runtime tolerance (seconds)"
                            value={editing.runtimeToleranceSeconds}
                            onChange={v => setEditing({ ...editing, runtimeToleranceSeconds: v })}
                            hint="0 disables runtime checking. Otherwise, files inside the same group must be within this many seconds of each other — useful for splitting trailers/extras from the main film."
                        />
                    </SettingsSection>

                    <SettingsSection title="Video score — resolution" description="Higher points mean preferred. The top-scoring part is highlighted as 'Best'.">
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                            <NumberField label="4K / 2160p" value={editing.scoreResolution4k} onChange={v => setEditing({ ...editing, scoreResolution4k: v })} />
                            <NumberField label="1080p" value={editing.scoreResolution1080} onChange={v => setEditing({ ...editing, scoreResolution1080: v })} />
                            <NumberField label="720p" value={editing.scoreResolution720} onChange={v => setEditing({ ...editing, scoreResolution720: v })} />
                            <NumberField label="Other / SD" value={editing.scoreResolutionOther} onChange={v => setEditing({ ...editing, scoreResolutionOther: v })} />
                        </div>
                    </SettingsSection>

                    <SettingsSection title="Video score — source" description="Detected from the release name in the file. Remux (untouched disc copy) ranks above BluRay encode, which ranks above WEB-DL, WEBRip, HDTV and DVD.">
                        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                            <NumberField label="Remux" value={editing.scoreSourceRemux} onChange={v => setEditing({ ...editing, scoreSourceRemux: v })} />
                            <NumberField label="BluRay" value={editing.scoreSourceBluRay} onChange={v => setEditing({ ...editing, scoreSourceBluRay: v })} />
                            <NumberField label="WEB-DL" value={editing.scoreSourceWebDl} onChange={v => setEditing({ ...editing, scoreSourceWebDl: v })} />
                            <NumberField label="WEBRip" value={editing.scoreSourceWebRip} onChange={v => setEditing({ ...editing, scoreSourceWebRip: v })} />
                            <NumberField label="HDTV" value={editing.scoreSourceHdtv} onChange={v => setEditing({ ...editing, scoreSourceHdtv: v })} />
                            <NumberField label="DVD" value={editing.scoreSourceDvd} onChange={v => setEditing({ ...editing, scoreSourceDvd: v })} />
                        </div>
                    </SettingsSection>

                    <SettingsSection title="Video score — codec">
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                            <NumberField label="AV1" value={editing.scoreCodecAv1} onChange={v => setEditing({ ...editing, scoreCodecAv1: v })} />
                            <NumberField label="HEVC / H.265" value={editing.scoreCodecHevc} onChange={v => setEditing({ ...editing, scoreCodecHevc: v })} />
                            <NumberField label="VP9" value={editing.scoreCodecVp9} onChange={v => setEditing({ ...editing, scoreCodecVp9: v })} />
                            <NumberField label="H.264 / AVC" value={editing.scoreCodecH264} onChange={v => setEditing({ ...editing, scoreCodecH264: v })} />
                        </div>
                    </SettingsSection>

                    <SettingsSection title="Video score — HDR">
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                            <NumberField label="Dolby Vision" value={editing.scoreHdrDolbyVision} onChange={v => setEditing({ ...editing, scoreHdrDolbyVision: v })} />
                            <NumberField label="HDR10 / HDR" value={editing.scoreHdr} onChange={v => setEditing({ ...editing, scoreHdr: v })} />
                        </div>
                    </SettingsSection>

                    <SettingsSection title="Video score — audio track" description="Each video part takes the score of its highest-quality audio track.">
                        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                            <NumberField label="Lossless (TrueHD / DTS-HD / Atmos)" value={editing.scoreAudioLossless} onChange={v => setEditing({ ...editing, scoreAudioLossless: v })} />
                            <NumberField label="Surround (EAC3 / AC3 / 6+ channels)" value={editing.scoreAudioSurround} onChange={v => setEditing({ ...editing, scoreAudioSurround: v })} />
                            <NumberField label="Base (everything else)" value={editing.scoreAudioBase} onChange={v => setEditing({ ...editing, scoreAudioBase: v })} />
                        </div>
                    </SettingsSection>
                </>
            )}

            {showAudioSections && (
                <>
                    {isGlobalScope && <SectionHeader>Audio rules — Music</SectionHeader>}

                    <SettingsSection title="Music score — codec tier" description="Higher points mean preferred. Lossless beats lossy; AAC/Opus rank above MP3/OGG.">
                        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                            <NumberField
                                label="Lossless (FLAC, ALAC, WAV, APE, DSD)"
                                value={editing.scoreCodecMusicLossless}
                                onChange={v => setEditing({ ...editing, scoreCodecMusicLossless: v })}
                            />
                            <NumberField
                                label="Lossy high (AAC, Opus, M4A)"
                                value={editing.scoreCodecMusicLossyHigh}
                                onChange={v => setEditing({ ...editing, scoreCodecMusicLossyHigh: v })}
                            />
                            <NumberField
                                label="Lossy standard (MP3, OGG, WMA)"
                                value={editing.scoreCodecMusicLossyStandard}
                                onChange={v => setEditing({ ...editing, scoreCodecMusicLossyStandard: v })}
                            />
                        </div>
                    </SettingsSection>

                    <SettingsSection title="Music score — sample rate">
                        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                            <NumberField
                                label="High (≥ 88.2 kHz)"
                                value={editing.scoreSampleRateHi}
                                onChange={v => setEditing({ ...editing, scoreSampleRateHi: v })}
                            />
                            <NumberField
                                label="Standard (44.1 / 48 kHz)"
                                value={editing.scoreSampleRateStandard}
                                onChange={v => setEditing({ ...editing, scoreSampleRateStandard: v })}
                            />
                            <NumberField
                                label="Low (< 44.1 kHz)"
                                value={editing.scoreSampleRateLow}
                                onChange={v => setEditing({ ...editing, scoreSampleRateLow: v })}
                            />
                        </div>
                    </SettingsSection>

                    <SettingsSection title="Music score — file size tiebreaker">
                        <NumberField
                            label="File size divisor (MB)"
                            value={Math.round(editing.scoreFileSizeDivisor / (1024 * 1024))}
                            onChange={v => setEditing({ ...editing, scoreFileSizeDivisor: Math.max(1, v) * 1024 * 1024 })}
                            hint="File size (in bytes) is divided by this number and added to the score. Smaller divisor = file size matters more. Default 1 MB gives a ~500-point edge to a 500 MB FLAC over a 50 MB MP3."
                        />
                    </SettingsSection>
                </>
            )}

            {(showVideoSections || showAudioSections) && (
                <SettingsSection title="Bitrate divisor" description="Applies to both video and audio — the part's overall bitrate (bps) is divided by this number and added to its score.">
                    <NumberField
                        label="Bitrate divisor"
                        value={editing.scoreBitrateDivisor}
                        onChange={v => setEditing({ ...editing, scoreBitrateDivisor: Math.max(1, v) })}
                        hint="Smaller divisor = bitrate matters more."
                    />
                </SettingsSection>
            )}
        </div>
    );
}

function SectionHeader({ children }: { children: React.ReactNode }) {
    return (
        <div className="pt-4 pb-1">
            <h3 className="text-sm font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">{children}</h3>
        </div>
    );
}

function IgnoredTab({ dialog, serverId }: { dialog: DialogApi, serverId?: string }) {
    const [ignored, setIgnored] = useState<DedupeIgnoredGroupVM[]>([]);
    const [loading, setLoading] = useState(true);

    const load = useCallback(async () => {
        setLoading(true);
        try {
            const data = await adminService.getIgnoredDuplicates(serverId);
            setIgnored(data);
        } catch (err) {
            console.error(err);
            await dialog.alert('Failed to load ignored groups.');
        } finally {
            setLoading(false);
        }
    }, [serverId, dialog]);

    useEffect(() => { load(); }, [load]);

    const handleUnignore = async (item: DedupeIgnoredGroupVM) => {
        if (!await dialog.confirm(`Restore "${item.title}" (${item.resolution}) to the duplicates list?`)) return;
        try {
            await adminService.unignoreDuplicateGroup(item.id, serverId);
            setIgnored(prev => prev.filter(i => i.id !== item.id));
        } catch (err) {
            console.error(err);
            await dialog.alert('Failed to unignore group.');
        }
    };

    if (loading) {
        return <div className="vora-skeleton h-48" />;
    }

    if (ignored.length === 0) {
        return (
            <div className="vora-card">
                <EmptyState
                    title="Nothing ignored yet"
                    description="When you ignore a duplicate group, it'll show up here so you can restore it later."
                    icon={<svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a9.97 9.97 0 011.563-3.029m5.858.908a3 3 0 114.243 4.243M9.878 9.878l4.242 4.242M9.88 9.88l-3.29-3.29m7.532 7.532l3.29 3.29M3 3l3.59 3.59m0 0A9.953 9.953 0 0112 5c4.478 0 8.268 2.943 9.543 7a10.025 10.025 0 01-4.132 5.411m0 0L21 21" /></svg>}
                />
            </div>
        );
    }

    return (
        <div className="space-y-3">
            {ignored.map(item => (
                <div key={item.id} className="vora-card p-4 flex justify-between items-start gap-4 flex-wrap">
                    <div className="min-w-0 flex-1">
                        <h3 className="text-sm font-semibold text-[var(--vora-text-primary)] truncate">{item.title}</h3>
                        <div className="flex gap-1.5 mt-1.5 flex-wrap">
                            <Chip tone="highlight">{item.type}</Chip>
                            <Chip tone="highlight">{item.resolution}</Chip>
                            <Chip>Ignored {new Date(item.ignoredAt).toLocaleDateString()}</Chip>
                        </div>
                        {item.note && (
                            <p className="text-xs text-[var(--vora-text-muted)] mt-2 italic">"{item.note}"</p>
                        )}
                    </div>
                    <button
                        type="button"
                        onClick={() => handleUnignore(item)}
                        className="vora-button-secondary text-xs shrink-0"
                    >
                        Unignore
                    </button>
                </div>
            ))}
        </div>
    );
}

function SettingsSection({ title, description, children }: { title: string, description?: string, children: React.ReactNode }) {
    return (
        <section className="vora-card p-5">
            <h3 className="text-base font-semibold text-[var(--vora-text-primary)]">{title}</h3>
            {description && <p className="text-xs text-[var(--vora-text-muted)] mt-0.5 mb-4">{description}</p>}
            {!description && <div className="mb-4" />}
            <div className="space-y-4">
                {children}
            </div>
        </section>
    );
}

function FieldLabel({ children }: { children: React.ReactNode }) {
    return <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">{children}</label>;
}

function NumberField({ label, value, onChange, hint }: { label: string, value: number, onChange: (v: number) => void, hint?: string }) {
    return (
        <div>
            <FieldLabel>{label}</FieldLabel>
            <input
                type="number"
                value={value}
                onChange={e => onChange(parseInt(e.target.value, 10) || 0)}
                className="vora-input max-w-xs"
            />
            {hint && <p className="text-xs text-[var(--vora-text-muted)] mt-1.5">{hint}</p>}
        </div>
    );
}

function Checkbox({ checked, onChange, label, hint }: { checked: boolean, onChange: (v: boolean) => void, label: string, hint?: string }) {
    return (
        <label className="flex items-start gap-3 cursor-pointer select-none">
            <input
                type="checkbox"
                checked={checked}
                onChange={e => onChange(e.target.checked)}
                className="w-4 h-4 accent-[var(--vora-accent-500)] mt-0.5 cursor-pointer"
            />
            <span className="flex flex-col">
                <span className="text-sm font-medium text-[var(--vora-text-primary)]">{label}</span>
                {hint && <span className="text-xs text-[var(--vora-text-muted)] mt-0.5">{hint}</span>}
            </span>
        </label>
    );
}
