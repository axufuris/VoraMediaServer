import { useState, useEffect, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { iptvAdminService, type IptvPlaylistVM, type IptvChannelKind } from '../../../api/Iptv/iptvAdminService';
import { iptvEpgAdminService, type IptvEpgSourceVM } from '../../../api/Iptv/iptvEpgAdminService';
import AdminIptvChannelsModal from '../../../components/Admin/IptvChannelsModal';
import IptvPlaylistEditModal from '../../../components/Admin/IptvPlaylistEditModal';
import IptvEpgSourceEditModal from '../../../components/Admin/IptvEpgSourceEditModal';
import IptvEpgDiagnosticsModal from '../../../components/Admin/IptvEpgDiagnosticsModal';
import FeatureToggle from '../../../components/Admin/Features/FeatureToggle';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';
import HealthBadge from '../../../components/Admin/Primitives/HealthBadge';
import { useDialog } from '../../../dialogs';

interface FreePlaylist {
    name: string;
    m3u: string;
    supportsWeb: boolean;
    maxConnections: number;
    defaultKind: IptvChannelKind;
}

interface FreeEpgSource {
    name: string;
    xml: string;
}

const FREE_PLAYLISTS: FreePlaylist[] = [
    // TV — Major free ad-supported (FAST) services. Streams are US-geo-locked.
    { name: "US — Pluto TV", m3u: "https://raw.githubusercontent.com/BuddyChewChew/app-m3u-generator/refs/heads/main/playlists/plutotv_us.m3u", supportsWeb: true, maxConnections: 0, defaultKind: "Tv" },
    { name: "US — Samsung TV Plus", m3u: "https://raw.githubusercontent.com/BuddyChewChew/app-m3u-generator/refs/heads/main/playlists/samsungtvplus_us.m3u", supportsWeb: true, maxConnections: 0, defaultKind: "Tv" },
    { name: "Roku Channel (all regions)", m3u: "https://raw.githubusercontent.com/BuddyChewChew/app-m3u-generator/refs/heads/main/playlists/roku_all.m3u", supportsWeb: true, maxConnections: 0, defaultKind: "Tv" },
    // TV — Kitchen-sink fallback (1351 channels, low EPG coverage).
    { name: "US — IPTV Org (full country)", m3u: "https://iptv-org.github.io/iptv/countries/us.m3u", supportsWeb: true, maxConnections: 0, defaultKind: "Tv" },
    // TV — Greece — iptv-org country playlist (pairs with GreekTVApp + epgshare01 GR1 EPGs).
    { name: "Greece — IPTV Org", m3u: "https://iptv-org.github.io/iptv/countries/gr.m3u", supportsWeb: true, maxConnections: 0, defaultKind: "Tv" },
    { name: "Greece — Free-Greek-IPTV", m3u: "https://raw.githubusercontent.com/free-greek-iptv/greek-iptv/master/android.m3u", supportsWeb: true, maxConnections: 0, defaultKind: "Tv" },
    // Radio — Radio Browser's live API. Endpoints generate up-to-date M3Us; hidebroken=true filters dead streams.
    { name: "Radio — Top 100 Worldwide (Radio Browser)", m3u: "https://de1.api.radio-browser.info/m3u/stations/topclick/100", supportsWeb: true, maxConnections: 0, defaultKind: "Radio" },
    { name: "Radio — Top 200 Most Voted (Radio Browser)", m3u: "https://de1.api.radio-browser.info/m3u/stations/topvote/200", supportsWeb: true, maxConnections: 0, defaultKind: "Radio" },
    { name: "Radio — US Top 100 (Radio Browser)", m3u: "https://de1.api.radio-browser.info/m3u/stations/bycountrycodeexact/US?limit=100&order=clickcount&reverse=true&hidebroken=true", supportsWeb: true, maxConnections: 0, defaultKind: "Radio" },
    { name: "Radio — News (Radio Browser)", m3u: "https://de1.api.radio-browser.info/m3u/stations/bytag/news?limit=100&order=clickcount&reverse=true&hidebroken=true", supportsWeb: true, maxConnections: 0, defaultKind: "Radio" },
    { name: "Radio — Classical (Radio Browser)", m3u: "https://de1.api.radio-browser.info/m3u/stations/bytag/classical?limit=100&order=clickcount&reverse=true&hidebroken=true", supportsWeb: true, maxConnections: 0, defaultKind: "Radio" },
    { name: "Radio — Jazz (Radio Browser)", m3u: "https://de1.api.radio-browser.info/m3u/stations/bytag/jazz?limit=100&order=clickcount&reverse=true&hidebroken=true", supportsWeb: true, maxConnections: 0, defaultKind: "Radio" }
];

const FREE_EPG_SOURCES: FreeEpgSource[] = [
    // US FAST-service EPGs — these are purpose-built to match the BuddyChewChew playlists above (mjh.nz convention).
    { name: "US — Pluto TV (EPG)", xml: "https://i.mjh.nz/PlutoTV/us.xml.gz" },
    { name: "US — Samsung TV Plus (EPG)", xml: "https://i.mjh.nz/SamsungTVPlus/us.xml.gz" },
    { name: "US — Roku Channel (EPG)", xml: "https://i.mjh.nz/Roku/all.xml.gz" },
    // US — Broadcast / cable EPG (best fallback for iptv-org US channels).
    { name: "US — IPTV-EPG.org Guide", xml: "https://iptv-epg.org/files/epg-us.xml" },
    // Greece EPGs.
    { name: "Greece — GreekTVApp EPG", xml: "https://ext.greektv.app/epg/epg.xml.gz" },
    { name: "Greece — EPG Share GR1", xml: "https://epgshare01.online/epgshare01/epg_ripper_GR1.xml.gz" },
    { name: "Greece — IPTV-EPG.org Guide", xml: "https://iptv-epg.org/files/epg-gr.xml" }
];

interface IptvPageProps {
    kind: IptvChannelKind;
}

export default function IptvPage({ kind }: IptvPageProps) {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();

    const isRadio = kind === 'Radio';
    const pageTitle = isRadio ? 'Internet Radio' : 'Live TV';
    const playlistNoun = isRadio ? 'Radio Playlist' : 'Live TV Playlist';
    const playlistNounPlural = isRadio ? 'Radio Playlists' : 'Live TV Playlists';
    const playlistDescription = isRadio
        ? 'A playlist is an M3U source that supplies radio stations.'
        : 'A playlist is an M3U source that supplies channels. Programme guide data is layered on separately from EPG sources below.';
    const filteredFreePlaylists = FREE_PLAYLISTS.filter(p => p.defaultKind === kind);

    const [playlists, setPlaylists] = useState<IptvPlaylistVM[]>([]);
    const [epgSources, setEpgSources] = useState<IptvEpgSourceVM[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [isSavingPlaylist, setIsSavingPlaylist] = useState(false);
    const [isSavingEpg, setIsSavingEpg] = useState(false);
    const [refreshingPlaylistIds, setRefreshingPlaylistIds] = useState<Set<string>>(new Set());
    const [refreshingEpgIds, setRefreshingEpgIds] = useState<Set<string>>(new Set());
    const [isRefreshingAllPlaylists, setIsRefreshingAllPlaylists] = useState(false);
    const [isRefreshingAllEpg, setIsRefreshingAllEpg] = useState(false);

    const [playlistQuickAdd, setPlaylistQuickAdd] = useState('');
    const [playlistName, setPlaylistName] = useState('');
    const [m3uUrl, setM3uUrl] = useState('');
    const [supportsWebPlayback, setSupportsWebPlayback] = useState(true);
    const [maxConcurrentStreams, setMaxConcurrentStreams] = useState(0);

    const [epgQuickAdd, setEpgQuickAdd] = useState('');
    const [epgName, setEpgName] = useState('');
    const [xmlTvUrl, setXmlTvUrl] = useState('');
    const [epgPriority, setEpgPriority] = useState(0);

    const [isChannelsModalOpen, setIsChannelsModalOpen] = useState(false);
    const [selectedPlaylist, setSelectedPlaylist] = useState<IptvPlaylistVM | null>(null);

    const [editingPlaylist, setEditingPlaylist] = useState<IptvPlaylistVM | null>(null);
    const [editingEpgSource, setEditingEpgSource] = useState<IptvEpgSourceVM | null>(null);
    const [showDiagnostics, setShowDiagnostics] = useState(false);

    const loadData = useCallback(async () => {
        try {
            const [playlistData, epgData] = await Promise.all([
                iptvAdminService.getPlaylists(serverId, kind),
                isRadio ? Promise.resolve([] as IptvEpgSourceVM[]) : iptvEpgAdminService.getSources(serverId)
            ]);
            setPlaylists(playlistData);
            setEpgSources(epgData);
        } catch (error) {
            console.error("Failed to load IPTV data", error);
        } finally {
            setIsLoading(false);
        }
    }, [serverId, kind, isRadio]);

    useEffect(() => {
        loadData();
    }, [loadData]);

    const handlePlaylistQuickSelect = (e: React.ChangeEvent<HTMLSelectElement>) => {
        const val = e.target.value;
        setPlaylistQuickAdd(val);

        const selected = filteredFreePlaylists.find(p => p.name === val);
        if (selected) {
            setPlaylistName(selected.name);
            setM3uUrl(selected.m3u);
            setSupportsWebPlayback(selected.supportsWeb);
            setMaxConcurrentStreams(selected.maxConnections);
        }
    };

    const handleEpgQuickSelect = (e: React.ChangeEvent<HTMLSelectElement>) => {
        const val = e.target.value;
        setEpgQuickAdd(val);

        const selected = FREE_EPG_SOURCES.find(s => s.name === val);
        if (selected) {
            setEpgName(selected.name);
            setXmlTvUrl(selected.xml);
        }
    };

    const handleAddPlaylist = async () => {
        if (!playlistName || !m3uUrl) {
            await dialog.alert({ title: "Validation Error", message: "Both a Playlist Name and an M3U Playlist URL are required." });
            return;
        }

        setIsSavingPlaylist(true);
        try {
            await iptvAdminService.addPlaylist(playlistName, m3uUrl, supportsWebPlayback, maxConcurrentStreams, kind, serverId);
            setPlaylistName('');
            setM3uUrl('');
            setSupportsWebPlayback(true);
            setMaxConcurrentStreams(0);
            setPlaylistQuickAdd('');
            await loadData();
        } catch (error) {
            console.error("Failed to add playlist", error);
            await dialog.alert({ title: "Error", message: "Failed to add playlist." });
        } finally {
            setIsSavingPlaylist(false);
        }
    };

    const handleRefreshPlaylist = async (id: string) => {
        setRefreshingPlaylistIds(prev => {
            const next = new Set(prev);
            next.add(id);
            return next;
        });
        try {
            await iptvAdminService.refreshPlaylist(id, serverId);
            await loadData();
        } catch (error) {
            console.error("Failed to refresh playlist", error);
            await dialog.alert({ title: "Error", message: "Failed to trigger playlist refresh." });
        } finally {
            setRefreshingPlaylistIds(prev => {
                const next = new Set(prev);
                next.delete(id);
                return next;
            });
        }
    };

    const handleRefreshAllPlaylists = async () => {
        if (playlists.length === 0 || isRefreshingAllPlaylists) return;
        setIsRefreshingAllPlaylists(true);
        setRefreshingPlaylistIds(new Set(playlists.map(p => p.id)));
        try {
            await Promise.all(playlists.map(p => iptvAdminService.refreshPlaylist(p.id, serverId)));
            await loadData();
        } catch (error) {
            console.error("Failed to refresh all playlists", error);
            await dialog.alert({ title: "Error", message: "One or more playlists failed to refresh. Check the table for details." });
        } finally {
            setRefreshingPlaylistIds(new Set());
            setIsRefreshingAllPlaylists(false);
        }
    };

    const handleDeletePlaylist = async (id: string) => {
        const confirmed = await dialog.confirm({
            title: "Delete Playlist",
            message: "Are you sure you want to delete this playlist? All associated channels will be removed.",
            tone: 'danger',
            confirmText: 'Delete'
        });
        if (!confirmed) return;

        try {
            await iptvAdminService.deletePlaylist(id, serverId);
            setPlaylists(prev => prev.filter(p => p.id !== id));
        } catch (error) {
            console.error("Failed to delete playlist", error);
            await dialog.alert({ title: "Error", message: "Failed to delete playlist." });
        }
    };

    const handleAddEpgSource = async () => {
        if (!epgName || !xmlTvUrl) {
            await dialog.alert({ title: "Validation Error", message: "Both a Source Name and an XMLTV URL are required." });
            return;
        }

        setIsSavingEpg(true);
        try {
            await iptvEpgAdminService.addSource(epgName, xmlTvUrl, epgPriority, serverId);
            setEpgName('');
            setXmlTvUrl('');
            setEpgPriority(0);
            setEpgQuickAdd('');
            await loadData();
        } catch (error) {
            console.error("Failed to add EPG source", error);
            await dialog.alert({ title: "Error", message: "Failed to add EPG source." });
        } finally {
            setIsSavingEpg(false);
        }
    };

    const handleRefreshEpgSource = async (id: string) => {
        setRefreshingEpgIds(prev => {
            const next = new Set(prev);
            next.add(id);
            return next;
        });
        try {
            await iptvEpgAdminService.refreshSource(id, serverId);
            await loadData();
        } catch (error) {
            console.error("Failed to refresh EPG source", error);
            await dialog.alert({ title: "Error", message: "Failed to trigger EPG refresh." });
        } finally {
            setRefreshingEpgIds(prev => {
                const next = new Set(prev);
                next.delete(id);
                return next;
            });
        }
    };

    const handleRefreshAllEpgSources = async () => {
        if (epgSources.length === 0 || isRefreshingAllEpg) return;
        setIsRefreshingAllEpg(true);
        setRefreshingEpgIds(new Set(epgSources.map(s => s.id)));
        try {
            // Refreshing any one source already triggers a global re-sync,
            // so a single call is enough to refresh everything.
            await iptvEpgAdminService.refreshSource(epgSources[0].id, serverId);
            await loadData();
        } catch (error) {
            console.error("Failed to refresh EPG sources", error);
            await dialog.alert({ title: "Error", message: "EPG re-sync failed. Check the table for per-source errors." });
        } finally {
            setRefreshingEpgIds(new Set());
            setIsRefreshingAllEpg(false);
        }
    };

    const handleDeleteEpgSource = async (id: string) => {
        const confirmed = await dialog.confirm({
            title: "Delete EPG Source",
            message: "Are you sure you want to delete this EPG source? Cached programme data from this source will be cleared on the next sync.",
            tone: 'danger',
            confirmText: 'Delete'
        });
        if (!confirmed) return;

        try {
            await iptvEpgAdminService.deleteSource(id, serverId);
            setEpgSources(prev => prev.filter(s => s.id !== id));
        } catch (error) {
            console.error("Failed to delete EPG source", error);
            await dialog.alert({ title: "Error", message: "Failed to delete EPG source." });
        }
    };

    if (isLoading) return (
        <div data-vora-page="">
            <PageHeader title={pageTitle} />
            <div className="px-8 max-w-[1400px] mx-auto"><div className="vora-skeleton h-64" /></div>
        </div>
    );

    return (
        <div data-vora-page="">
            <PageHeader
                title={pageTitle}
                description={isRadio ? 'Add radio playlists and configure providers.' : 'Manage IPTV playlists and electronic program guide (EPG) sources.'}
            />

            <div className="px-8 pt-6 pb-10 max-w-[1400px] mx-auto">
                <FeatureToggle
                    featureKey={isRadio ? 'internetRadio' : 'liveTv'}
                    label={`Enable ${pageTitle}`}
                    description={isRadio
                        ? 'When off, the Audio hub Radio tab is hidden from clients and radio playlists are filtered out of the IPTV client endpoint.'
                        : 'When off, the Live TV nav entry is hidden from clients, the program guide endpoint returns 403, and live TV playlists are filtered out of the IPTV client endpoint. DVR is also disabled when Live TV is off.'}
                    serverId={serverId}
                />

                <section className="vora-card p-6 mb-6">
                    <h2 className="text-base font-semibold text-[var(--vora-text-primary)] mb-1">Add {playlistNoun}</h2>
                    <p className="text-xs text-[var(--vora-text-muted)] mb-4">{playlistDescription}</p>

                    {filteredFreePlaylists.length > 0 && (
                        <div className="mb-5">
                            <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">Quick add free {playlistNoun}</label>
                            <select
                                value={playlistQuickAdd}
                                onChange={handlePlaylistQuickSelect}
                                className="vora-input cursor-pointer"
                            >
                                <option value="">Select a free {isRadio ? 'radio' : 'playlist'} to autofill…</option>
                                {filteredFreePlaylists.map(p => (
                                    <option key={p.name} value={p.name}>{p.name}</option>
                                ))}
                            </select>
                        </div>
                    )}

                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mb-4">
                        <div>
                            <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">{playlistNoun} name</label>
                            <input
                                type="text"
                                value={playlistName}
                                onChange={e => setPlaylistName(e.target.value)}
                                className="vora-input"
                                placeholder={isRadio ? 'e.g. Top 100 Radio' : 'e.g. Pluto TV'}
                            />
                        </div>
                        <div>
                            <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">M3U URL</label>
                            <input
                                type="text"
                                value={m3uUrl}
                                onChange={e => setM3uUrl(e.target.value)}
                                className="vora-input font-mono text-sm"
                                placeholder="https://…"
                            />
                        </div>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mb-5">
                        <label className="flex items-center gap-3 cursor-pointer bg-[var(--vora-bg-sunken)] p-3 rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] select-none">
                            <input
                                type="checkbox"
                                checked={supportsWebPlayback}
                                onChange={e => setSupportsWebPlayback(e.target.checked)}
                                className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                            />
                            <div className="flex flex-col">
                                <span className="text-sm font-semibold text-[var(--vora-text-primary)]">Supports web browser playback</span>
                                <span className="text-xs text-[var(--vora-text-muted)]">Uncheck if this playlist's streams block browsers with CORS.</span>
                            </div>
                        </label>

                        <div className="bg-[var(--vora-bg-sunken)] p-3 rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] flex items-center justify-between gap-3">
                            <div className="flex flex-col min-w-0">
                                <span className="text-sm font-semibold text-[var(--vora-text-primary)]">Max concurrent streams (tuners)</span>
                                <span className="text-xs text-[var(--vora-text-muted)]">0 = unlimited. Set this to prevent premium provider bans.</span>
                            </div>
                            <input
                                type="number"
                                min={0}
                                value={maxConcurrentStreams}
                                onChange={e => setMaxConcurrentStreams(parseInt(e.target.value) || 0)}
                                className="vora-input w-20 text-center font-semibold"
                            />
                        </div>
                    </div>

                    <div className="flex justify-end">
                        <button
                            type="button"
                            onClick={handleAddPlaylist}
                            disabled={isSavingPlaylist || !playlistName || !m3uUrl}
                            className="vora-button-primary"
                        >
                            {isSavingPlaylist ? 'Adding & parsing…' : `Add ${playlistNoun}`}
                        </button>
                    </div>
                </section>

                <section className="vora-card overflow-hidden mb-10">
                    <div className="bg-[var(--vora-bg-sunken)] px-5 py-3 border-b border-[var(--vora-border-subtle)] flex justify-between items-center">
                        <span className="text-xs text-[var(--vora-text-muted)] font-bold uppercase tracking-widest">
                            {playlistNounPlural} ({playlists.length})
                        </span>
                        <button
                            type="button"
                            onClick={handleRefreshAllPlaylists}
                            disabled={playlists.length === 0 || isRefreshingAllPlaylists}
                            className="text-xs font-semibold px-3 py-1 rounded-[var(--vora-radius-md)] text-[var(--vora-info-text)] bg-[var(--vora-info-soft)] hover:bg-[var(--vora-info-500)] hover:text-[var(--vora-text-primary)] cursor-pointer transition-colors disabled:opacity-50"
                        >
                            {isRefreshingAllPlaylists ? 'Refreshing all…' : 'Refresh all'}
                        </button>
                    </div>
                    <div className="overflow-x-auto">
                        <table className="w-full text-left">
                            <thead className="bg-[var(--vora-bg-sunken)] border-b border-[var(--vora-border-subtle)] text-[11px] uppercase tracking-wider text-[var(--vora-text-muted)]">
                                <tr>
                                    <th className="px-4 py-3 font-semibold w-1/4">Name</th>
                                    <th className="px-4 py-3 font-semibold w-1/4">M3U URL</th>
                                    <th className="px-4 py-3 font-semibold">Channels</th>
                                    <th className="px-4 py-3 font-semibold">Limit</th>
                                    <th className="px-4 py-3 font-semibold">Last Synced</th>
                                    <th className="px-4 py-3 font-semibold text-right">Actions</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-[var(--vora-border-subtle)]">
                                {playlists.map(p => (
                                    <tr key={p.id} className="hover:bg-[var(--vora-bg-sunken)]/40 transition-colors">
                                        <td className="px-4 py-3 align-top">
                                            <div className="flex items-center gap-2 font-semibold text-[var(--vora-text-primary)]">
                                                {p.name}
                                                {!p.supportsWebPlayback && <HealthBadge tone="neutral" showDot={false}>Native only</HealthBadge>}
                                            </div>
                                            {p.lastError && (
                                                <div className="text-[var(--vora-danger-text)] text-xs mt-1.5 bg-[var(--vora-danger-soft)] p-1.5 rounded border border-[var(--vora-danger-500)]/20">
                                                    <span className="font-bold">Error:</span> {p.lastError}
                                                </div>
                                            )}
                                        </td>
                                        <td className="px-4 py-3 text-[var(--vora-text-secondary)] text-sm align-top font-mono break-all">{p.m3uUrl}</td>
                                        <td className="px-4 py-3 text-[var(--vora-text-secondary)] align-top tabular-nums">{p.channels?.length || 0}</td>
                                        <td className="px-4 py-3 text-[var(--vora-text-secondary)] align-top">{p.maxConcurrentStreams === 0 ? 'Unlimited' : p.maxConcurrentStreams}</td>
                                        <td className="px-4 py-3 text-[var(--vora-text-secondary)] align-top text-xs">{p.lastSyncedAt ? new Date(p.lastSyncedAt).toLocaleString() : <span className="italic text-[var(--vora-text-disabled)]">never</span>}</td>
                                        <td className="px-4 py-3 text-right align-top">
                                            <div className="flex justify-end gap-1.5">
                                                <button
                                                    type="button"
                                                    onClick={() => { setSelectedPlaylist(p); setIsChannelsModalOpen(true); }}
                                                    className="text-xs font-semibold px-2.5 py-1 rounded-[var(--vora-radius-md)] text-[var(--vora-accent-text)] bg-[var(--vora-accent-soft)] hover:bg-[var(--vora-accent-soft-hover)] cursor-pointer transition-colors"
                                                >
                                                    Channels
                                                </button>
                                                <button
                                                    type="button"
                                                    onClick={() => setEditingPlaylist(p)}
                                                    className="text-xs font-semibold px-2.5 py-1 rounded-[var(--vora-radius-md)] text-[var(--vora-text-primary)] bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-border-strong)] cursor-pointer transition-colors"
                                                >
                                                    Edit
                                                </button>
                                                <button
                                                    type="button"
                                                    onClick={() => handleRefreshPlaylist(p.id)}
                                                    disabled={refreshingPlaylistIds.has(p.id)}
                                                    className="text-xs font-semibold px-2.5 py-1 rounded-[var(--vora-radius-md)] text-[var(--vora-info-text)] bg-[var(--vora-info-soft)] hover:bg-[var(--vora-info-500)] hover:text-[var(--vora-text-primary)] cursor-pointer transition-colors disabled:opacity-50"
                                                >
                                                    {refreshingPlaylistIds.has(p.id) ? 'Refreshing…' : 'Refresh'}
                                                </button>
                                                <button
                                                    type="button"
                                                    onClick={() => handleDeletePlaylist(p.id)}
                                                    className="text-xs font-semibold px-2.5 py-1 rounded-[var(--vora-radius-md)] text-[var(--vora-danger-text)] bg-[var(--vora-danger-soft)] hover:bg-[var(--vora-danger-500)] hover:text-[var(--vora-text-primary)] cursor-pointer transition-colors"
                                                >
                                                    Delete
                                                </button>
                                            </div>
                                        </td>
                                    </tr>
                                ))}
                                {playlists.length === 0 && (
                                    <tr><td colSpan={6} className="px-4 py-8 text-center text-[var(--vora-text-muted)]">No {playlistNounPlural.toLowerCase()} added yet.</td></tr>
                                )}
                            </tbody>
                        </table>
                    </div>
                </section>

                {!isRadio && (
                <>
                <section className="vora-card p-6 mb-6">
                    <h2 className="text-base font-semibold text-[var(--vora-text-primary)] mb-1">Add EPG Source</h2>
                    <p className="text-xs text-[var(--vora-text-muted)] mb-4">EPG sources supply programme guide data. They are merged globally by channel ID (tvg-id) across all playlists, so a single XMLTV feed can enrich any number of playlists at once. Lower priority numbers sync first.</p>

                    <div className="mb-5">
                        <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">Quick add free EPG source</label>
                        <select
                            value={epgQuickAdd}
                            onChange={handleEpgQuickSelect}
                            className="vora-input cursor-pointer"
                        >
                            <option value="">Select a free EPG source to autofill…</option>
                            {FREE_EPG_SOURCES.map(s => (
                                <option key={s.name} value={s.name}>{s.name}</option>
                            ))}
                        </select>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-5">
                        <div>
                            <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">Source name</label>
                            <input type="text" value={epgName} onChange={e => setEpgName(e.target.value)} className="vora-input" placeholder="e.g. US Sports Bundle" />
                        </div>
                        <div>
                            <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">XMLTV EPG URL</label>
                            <input type="text" value={xmlTvUrl} onChange={e => setXmlTvUrl(e.target.value)} className="vora-input font-mono text-sm" placeholder="https://…" />
                        </div>
                        <div>
                            <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">Priority (lower = first)</label>
                            <input type="number" value={epgPriority} onChange={e => setEpgPriority(parseInt(e.target.value) || 0)} className="vora-input text-center font-semibold" />
                        </div>
                    </div>

                    <div className="flex justify-end">
                        <button
                            type="button"
                            onClick={handleAddEpgSource}
                            disabled={isSavingEpg || !epgName || !xmlTvUrl}
                            className="vora-button-primary"
                        >
                            {isSavingEpg ? 'Adding & syncing…' : 'Add EPG source'}
                        </button>
                    </div>
                </section>

                <section className="vora-card overflow-hidden">
                    <div className="bg-[var(--vora-bg-sunken)] px-5 py-3 border-b border-[var(--vora-border-subtle)] flex justify-between items-center">
                        <span className="text-xs text-[var(--vora-text-muted)] font-bold uppercase tracking-widest">EPG sources ({epgSources.length})</span>
                        <div className="flex items-center gap-2">
                            <button
                                type="button"
                                onClick={() => setShowDiagnostics(true)}
                                className="text-xs font-semibold px-3 py-1 rounded-[var(--vora-radius-md)] text-[var(--vora-warning-text)] bg-[var(--vora-warning-soft)] hover:bg-[var(--vora-warning-500)] hover:text-[var(--vora-text-primary)] cursor-pointer transition-colors"
                            >
                                Match diagnostics
                            </button>
                            <button
                                type="button"
                                onClick={handleRefreshAllEpgSources}
                                disabled={epgSources.length === 0 || isRefreshingAllEpg}
                                className="text-xs font-semibold px-3 py-1 rounded-[var(--vora-radius-md)] text-[var(--vora-info-text)] bg-[var(--vora-info-soft)] hover:bg-[var(--vora-info-500)] hover:text-[var(--vora-text-primary)] cursor-pointer transition-colors disabled:opacity-50"
                            >
                                {isRefreshingAllEpg ? 'Refreshing all…' : 'Refresh all'}
                            </button>
                        </div>
                    </div>
                    <div className="overflow-x-auto">
                        <table className="w-full text-left">
                            <thead className="bg-[var(--vora-bg-sunken)] border-b border-[var(--vora-border-subtle)] text-[11px] uppercase tracking-wider text-[var(--vora-text-muted)]">
                                <tr>
                                    <th className="px-4 py-3 font-semibold w-1/4">Name</th>
                                    <th className="px-4 py-3 font-semibold w-1/3">XMLTV URL</th>
                                    <th className="px-4 py-3 font-semibold">Priority</th>
                                    <th className="px-4 py-3 font-semibold">Last Synced</th>
                                    <th className="px-4 py-3 font-semibold text-right">Actions</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-[var(--vora-border-subtle)]">
                                {epgSources.map(s => (
                                    <tr key={s.id} className="hover:bg-[var(--vora-bg-sunken)]/40 transition-colors">
                                        <td className="px-4 py-3 align-top">
                                            <div className="flex items-center gap-2 font-semibold text-[var(--vora-text-primary)]">
                                                {s.name}
                                                {!s.isActive && <HealthBadge tone="neutral" showDot={false}>Disabled</HealthBadge>}
                                            </div>
                                            {s.lastError && (
                                                <div className="text-[var(--vora-danger-text)] text-xs mt-1.5 bg-[var(--vora-danger-soft)] p-1.5 rounded border border-[var(--vora-danger-500)]/20">
                                                    <span className="font-bold">Error:</span> {s.lastError}
                                                </div>
                                            )}
                                        </td>
                                        <td className="px-4 py-3 text-[var(--vora-text-secondary)] text-sm align-top font-mono break-all">{s.xmlTvUrl}</td>
                                        <td className="px-4 py-3 text-[var(--vora-text-secondary)] align-top tabular-nums">{s.priority}</td>
                                        <td className="px-4 py-3 text-[var(--vora-text-secondary)] align-top text-xs">{s.lastSyncedAt ? new Date(s.lastSyncedAt).toLocaleString() : <span className="italic text-[var(--vora-text-disabled)]">never</span>}</td>
                                        <td className="px-4 py-3 text-right align-top">
                                            <div className="flex justify-end gap-1.5">
                                                <button
                                                    type="button"
                                                    onClick={() => setEditingEpgSource(s)}
                                                    className="text-xs font-semibold px-2.5 py-1 rounded-[var(--vora-radius-md)] text-[var(--vora-text-primary)] bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-border-strong)] cursor-pointer transition-colors"
                                                >
                                                    Edit
                                                </button>
                                                <button
                                                    type="button"
                                                    onClick={() => handleRefreshEpgSource(s.id)}
                                                    disabled={refreshingEpgIds.has(s.id)}
                                                    className="text-xs font-semibold px-2.5 py-1 rounded-[var(--vora-radius-md)] text-[var(--vora-info-text)] bg-[var(--vora-info-soft)] hover:bg-[var(--vora-info-500)] hover:text-[var(--vora-text-primary)] cursor-pointer transition-colors disabled:opacity-50"
                                                >
                                                    {refreshingEpgIds.has(s.id) ? 'Refreshing…' : 'Refresh'}
                                                </button>
                                                <button
                                                    type="button"
                                                    onClick={() => handleDeleteEpgSource(s.id)}
                                                    className="text-xs font-semibold px-2.5 py-1 rounded-[var(--vora-radius-md)] text-[var(--vora-danger-text)] bg-[var(--vora-danger-soft)] hover:bg-[var(--vora-danger-500)] hover:text-[var(--vora-text-primary)] cursor-pointer transition-colors"
                                                >
                                                    Delete
                                                </button>
                                            </div>
                                        </td>
                                    </tr>
                                ))}
                                {epgSources.length === 0 && (
                                    <tr><td colSpan={5} className="px-4 py-8 text-center text-[var(--vora-text-muted)]">No EPG sources added yet.</td></tr>
                                )}
                            </tbody>
                        </table>
                    </div>
                </section>
                </>
                )}
            </div>

            {selectedPlaylist && (
                <AdminIptvChannelsModal
                    isOpen={isChannelsModalOpen}
                    onClose={() => { setIsChannelsModalOpen(false); setSelectedPlaylist(null); }}
                    playlistName={selectedPlaylist.name}
                    channels={selectedPlaylist.channels || []}
                    serverId={serverId}
                    onChannelToggled={loadData}
                />
            )}

            {editingPlaylist && (
                <IptvPlaylistEditModal
                    isOpen={true}
                    onClose={() => setEditingPlaylist(null)}
                    playlist={editingPlaylist}
                    serverId={serverId}
                    onSaved={loadData}
                />
            )}

            {editingEpgSource && (
                <IptvEpgSourceEditModal
                    isOpen={true}
                    onClose={() => setEditingEpgSource(null)}
                    source={editingEpgSource}
                    serverId={serverId}
                    onSaved={loadData}
                />
            )}

            {showDiagnostics && (
                <IptvEpgDiagnosticsModal
                    isOpen={true}
                    onClose={() => setShowDiagnostics(false)}
                    serverId={serverId}
                />
            )}
        </div>
    );
}
