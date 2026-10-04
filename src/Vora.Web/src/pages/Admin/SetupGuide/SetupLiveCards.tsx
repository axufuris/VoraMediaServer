import { useState, type ReactNode } from 'react';
import { iptvAdminService, type IptvChannelKind, type IptvPlaylistVM } from '../../../api/Iptv/iptvAdminService';
import { iptvEpgAdminService, type IptvEpgSourceVM } from '../../../api/Iptv/iptvEpgAdminService';
import { podcastService, type CatalogPodcastVM, type DiscoveredPodcastVM } from '../../../api/Podcasts/podcastService';
import HealthBadge from '../../../components/Admin/Primitives/HealthBadge';
import { COUNTRY_OPTIONS } from '../../../utils/countries';
import { FREE_EPG_SOURCES, FREE_PLAYLISTS } from '../../../utils/iptvPresets';
import { CardHeading, CheckRow, FieldLabel, Hint, SetupCard, StatusMessage } from './SetupParts';

const looksLikeUrl = (value: string) => /^(https?|feed):/i.test(value.trim());

function AddedList({ items, onRemove }: { items: { id: string; name: string; detail: string; badge: ReactNode }[]; onRemove: (id: string) => void }) {
    if (items.length === 0) {
        return <div className="rounded-[var(--vora-radius-md)] border border-dashed border-[var(--vora-border-strong)] bg-[var(--vora-bg-sunken)] p-3.5 text-center text-[13px] text-[var(--vora-text-muted)]">Nothing added yet.</div>;
    }
    return (
        <ul className="overflow-hidden rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)]">
            {items.map(item => (
                <li key={item.id} className="flex min-w-0 items-center gap-3 border-t border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] px-3 py-2.5 first:border-t-0">
                    <div className="min-w-0 flex-1">
                        <div className="truncate font-semibold text-[var(--vora-text-primary)]">{item.name}</div>
                        <div className="truncate font-mono text-xs text-[var(--vora-text-muted)]">{item.detail}</div>
                    </div>
                    {item.badge}
                    <button type="button" onClick={() => onRemove(item.id)} className="cursor-pointer rounded-[var(--vora-radius-md)] px-2 py-1 text-xs font-semibold text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-surface)] hover:text-[var(--vora-text-primary)]">Remove</button>
                </li>
            ))}
        </ul>
    );
}

export function SetupPlaylistCard({ kind, playlists, serverId, onChanged }: { kind: IptvChannelKind; playlists: IptvPlaylistVM[]; serverId?: string; onChanged: () => void }) {
    const isTv = kind === 'Tv';
    const presets = FREE_PLAYLISTS.filter(p => p.defaultKind === kind);
    const [preset, setPreset] = useState('');
    const [name, setName] = useState('');
    const [url, setUrl] = useState('');
    const [supportsWeb, setSupportsWeb] = useState(true);
    const [maxStreams, setMaxStreams] = useState(0);
    const [country, setCountry] = useState('');
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const prefix = isTv ? 'setup-tv' : 'setup-radio';
    const unit = isTv ? 'channels' : 'stations';

    const choosePreset = (value: string) => {
        setPreset(value);
        const found = presets.find(p => p.name === value);
        if (found) {
            setName(found.name);
            setUrl(found.m3u);
            setSupportsWeb(found.supportsWeb);
            setMaxStreams(found.maxConnections);
        }
    };

    const add = async () => {
        if (!name.trim() || !url.trim()) {
            setError('Give it a name and an M3U URL first.');
            return;
        }
        setBusy(true);
        setError(null);
        try {
            await iptvAdminService.addPlaylist(name.trim(), url.trim(), supportsWeb, maxStreams, kind, isTv ? null : (country || null), false, serverId);
            setPreset(''); setName(''); setUrl(''); setCountry('');
            onChanged();
        } catch {
            setError(`Couldn't add that playlist. Check the URL and try again.`);
        } finally {
            setBusy(false);
        }
    };

    const remove = async (id: string) => {
        try {
            await iptvAdminService.deletePlaylist(id, serverId);
            onChanged();
        } catch {
            setError(`Couldn't remove that playlist.`);
        }
    };

    return (
        <SetupCard>
            <CardHeading title={isTv ? 'Add a channel playlist' : 'Add a station list'} subtitle={`An M3U playlist supplies the ${unit}. Adding one reads it straight away.`} />
            <div>
                <FieldLabel htmlFor={`${prefix}-preset`}>Start from a free list</FieldLabel>
                <select id={`${prefix}-preset`} className="vora-input cursor-pointer" value={preset} onChange={e => choosePreset(e.target.value)}>
                    <option value="">{isTv ? 'Select a free playlist to fill in…' : 'Select free radio to fill in…'}</option>
                    {presets.map(p => <option key={p.name} value={p.name}>{p.name}</option>)}
                </select>
            </div>
            <div className="flex flex-wrap gap-3">
                <div className="min-w-[200px] flex-1">
                    <FieldLabel htmlFor={`${prefix}-name`}>Name</FieldLabel>
                    <input id={`${prefix}-name`} className="vora-input" value={name} onChange={e => setName(e.target.value)} placeholder={isTv ? 'e.g. US — IPTV Org' : 'e.g. Top 100 Radio'} autoComplete="off" />
                </div>
                <div className="min-w-[260px] flex-[2]">
                    <FieldLabel htmlFor={`${prefix}-url`}>M3U URL</FieldLabel>
                    <input id={`${prefix}-url`} className="vora-input font-mono text-sm" value={url} onChange={e => setUrl(e.target.value)} placeholder="https://…" autoComplete="off" />
                </div>
            </div>
            {isTv ? (
                <div className="flex flex-wrap items-end gap-4">
                    <div className="min-w-[240px] flex-1">
                        <CheckRow id={`${prefix}-web`} checked={supportsWeb} onChange={setSupportsWeb} label="Plays in web browsers" hint="Untick if this provider's streams block browsers." />
                    </div>
                    <div className="w-36">
                        <FieldLabel htmlFor={`${prefix}-max`}>Max streams</FieldLabel>
                        <input id={`${prefix}-max`} type="number" min={0} className="vora-input" value={maxStreams} onChange={e => setMaxStreams(parseInt(e.target.value, 10) || 0)} />
                    </div>
                    <Hint>Max streams: 0 means no limit. Set it to your provider's connection limit to avoid being banned.</Hint>
                </div>
            ) : (
                <div>
                    <FieldLabel htmlFor={`${prefix}-country`}>Only show stations from</FieldLabel>
                    <select id={`${prefix}-country`} className="vora-input max-w-sm cursor-pointer" value={country} onChange={e => setCountry(e.target.value)}>
                        <option value="">All countries</option>
                        {COUNTRY_OPTIONS.map(c => <option key={c.code} value={c.code}>{c.name}</option>)}
                    </select>
                </div>
            )}
            <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="min-w-0 flex-1">{error && <StatusMessage tone="error">{error}</StatusMessage>}</div>
                <button type="button" onClick={add} disabled={busy} className="vora-button-primary !px-3 !py-1.5 text-xs">{busy ? `Adding and reading ${unit}…` : 'Add playlist'}</button>
            </div>
            <AddedList
                items={playlists.map(p => ({
                    id: p.id,
                    name: p.name,
                    detail: p.m3uUrl ?? '',
                    badge: p.lastError
                        ? <HealthBadge tone="error">Couldn't read</HealthBadge>
                        : <HealthBadge tone="ok" showDot={false}>{(p.channels?.length ?? 0).toLocaleString()} {unit}</HealthBadge>,
                }))}
                onRemove={remove}
            />
        </SetupCard>
    );
}

export function SetupGuideSourceCard({ sources, serverId, onChanged }: { sources: IptvEpgSourceVM[]; serverId?: string; onChanged: () => void }) {
    const [preset, setPreset] = useState('');
    const [name, setName] = useState('');
    const [url, setUrl] = useState('');
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const choosePreset = (value: string) => {
        setPreset(value);
        const found = FREE_EPG_SOURCES.find(s => s.name === value);
        if (found) { setName(found.name); setUrl(found.xml); }
    };

    const add = async () => {
        if (!name.trim() || !url.trim()) {
            setError('Give it a name and an XMLTV URL first.');
            return;
        }
        setBusy(true);
        setError(null);
        try {
            await iptvEpgAdminService.addSource(name.trim(), url.trim(), 0, serverId);
            setPreset(''); setName(''); setUrl('');
            onChanged();
        } catch {
            setError(`Couldn't add that guide. Check the URL and try again.`);
        } finally {
            setBusy(false);
        }
    };

    const remove = async (id: string) => {
        try {
            await iptvEpgAdminService.deleteSource(id, serverId);
            onChanged();
        } catch {
            setError(`Couldn't remove that guide.`);
        }
    };

    return (
        <SetupCard>
            <CardHeading title="Add a TV guide" subtitle="An XMLTV guide supplies what's on. One guide can cover channels from any number of playlists." />
            <div>
                <FieldLabel htmlFor="setup-epg-preset">Start from a free guide</FieldLabel>
                <select id="setup-epg-preset" className="vora-input cursor-pointer" value={preset} onChange={e => choosePreset(e.target.value)}>
                    <option value="">Select a free guide to fill in…</option>
                    {FREE_EPG_SOURCES.map(s => <option key={s.name} value={s.name}>{s.name}</option>)}
                </select>
            </div>
            <div className="flex flex-wrap gap-3">
                <div className="min-w-[200px] flex-1">
                    <FieldLabel htmlFor="setup-epg-name">Name</FieldLabel>
                    <input id="setup-epg-name" className="vora-input" value={name} onChange={e => setName(e.target.value)} placeholder="e.g. US guide" autoComplete="off" />
                </div>
                <div className="min-w-[260px] flex-[2]">
                    <FieldLabel htmlFor="setup-epg-url">XMLTV URL</FieldLabel>
                    <input id="setup-epg-url" className="vora-input font-mono text-sm" value={url} onChange={e => setUrl(e.target.value)} placeholder="https://…" autoComplete="off" />
                </div>
            </div>
            <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="min-w-0 flex-1">{error && <StatusMessage tone="error">{error}</StatusMessage>}</div>
                <button type="button" onClick={add} disabled={busy} className="vora-button-primary !px-3 !py-1.5 text-xs">{busy ? 'Adding and reading the guide…' : 'Add guide'}</button>
            </div>
            {sources.length === 0
                ? <div className="rounded-[var(--vora-radius-md)] border border-dashed border-[var(--vora-border-strong)] bg-[var(--vora-bg-sunken)] p-3.5 text-center text-[13px] text-[var(--vora-text-muted)]">No guide yet. Channels will play, but without listings.</div>
                : <AddedList
                    items={sources.map(s => ({ id: s.id, name: s.name, detail: s.xmlTvUrl, badge: s.lastError ? <HealthBadge tone="error">Couldn't read</HealthBadge> : <HealthBadge tone="ok">Added</HealthBadge> }))}
                    onRemove={remove}
                />}
        </SetupCard>
    );
}

export function SetupPodcastCatalogCard({ catalog, serverId, onChanged }: { catalog: CatalogPodcastVM[]; serverId?: string; onChanged: () => void }) {
    const [query, setQuery] = useState('');
    const [results, setResults] = useState<DiscoveredPodcastVM[] | null>(null);
    const [busy, setBusy] = useState(false);
    const [adding, setAdding] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);
    const inCatalog = new Set(catalog.map(c => c.feedUrl));

    const add = async (feedUrl: string) => {
        setAdding(feedUrl);
        setError(null);
        try {
            await podcastService.addToCatalog(feedUrl, serverId);
            onChanged();
        } catch {
            setError(`Couldn't add that show. Check the feed and try again.`);
        } finally {
            setAdding(null);
        }
    };

    const searchOrAdd = async () => {
        const text = query.trim();
        if (!text) return;
        if (looksLikeUrl(text)) {
            await add(text);
            setQuery('');
            return;
        }
        setBusy(true);
        setError(null);
        try {
            setResults(await podcastService.search(text, 12, serverId));
        } catch {
            setError('Search is not available right now.');
        } finally {
            setBusy(false);
        }
    };

    return (
        <SetupCard>
            <CardHeading title="Add shows to the catalog" subtitle="Search Apple Podcasts or paste a show's RSS feed. No key needed." badges={<HealthBadge tone="neutral" showDot={false}>{catalog.length} in catalog</HealthBadge>} />
            <form className="flex flex-wrap items-end gap-3" onSubmit={e => { e.preventDefault(); void searchOrAdd(); }}>
                <div className="min-w-[240px] flex-1">
                    <FieldLabel htmlFor="setup-podcast-query">Search or feed URL</FieldLabel>
                    <input id="setup-podcast-query" type="search" className="vora-input" value={query} onChange={e => setQuery(e.target.value)} placeholder="e.g. history, or https://…/feed.xml" autoComplete="off" />
                </div>
                <button type="submit" disabled={busy || !!adding} className="vora-button-secondary">{looksLikeUrl(query) ? 'Add feed' : busy ? 'Searching…' : 'Search'}</button>
            </form>
            {error && <StatusMessage tone="error">{error}</StatusMessage>}
            {results && (results.length === 0
                ? <div className="rounded-[var(--vora-radius-md)] border border-dashed border-[var(--vora-border-strong)] bg-[var(--vora-bg-sunken)] p-3.5 text-center text-[13px] text-[var(--vora-text-muted)]">No matches.</div>
                : <ul className="overflow-hidden rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)]">
                    {results.map(r => {
                        const added = inCatalog.has(r.feedUrl);
                        return (
                            <li key={r.feedUrl} className="grid grid-cols-[44px_minmax(0,1fr)_auto] items-center gap-3 border-t border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] px-3 py-2.5 first:border-t-0">
                                <div className="h-11 w-11 overflow-hidden rounded-[var(--vora-radius-md)] bg-[var(--vora-bg-raised)]">
                                    {r.artworkUrl && <img src={r.artworkUrl} alt="" className="h-full w-full object-cover" />}
                                </div>
                                <div className="min-w-0">
                                    <div className="truncate font-semibold text-[var(--vora-text-primary)]">{r.title}</div>
                                    {r.author && <div className="truncate text-xs text-[var(--vora-text-muted)]">{r.author}</div>}
                                </div>
                                <button type="button" disabled={added || adding === r.feedUrl} onClick={() => add(r.feedUrl)} className="vora-button-secondary !px-3 !py-1.5 text-xs">
                                    {added ? 'In catalog' : adding === r.feedUrl ? 'Adding…' : 'Add'}
                                </button>
                            </li>
                        );
                    })}
                </ul>)}
        </SetupCard>
    );
}
