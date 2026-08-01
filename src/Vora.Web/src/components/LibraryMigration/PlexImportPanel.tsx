import { useCallback, useEffect, useMemo, useState } from 'react';
import { LibrarySyncPinModal } from './LibrarySyncPinModal';
import { libraryImportService } from '../../api/LibraryMigration/libraryImportService';
import type {
    LibraryMigrationJobVM,
    LibrarySyncProviderVM,
    LibrarySyncTokenVM,
    RemoteLibraryVM,
    RemoteServerVM
} from '../../api/LibraryMigration/libraryMigrationService';
import { useSignalREvent } from '../../hooks/useSignalREvent';

interface PlexImportPanelProps {
    serverId?: string;
}

export default function PlexImportPanel({ serverId }: PlexImportPanelProps) {
    const [provider, setProvider] = useState<LibrarySyncProviderVM | null>(null);
    const [pinOpen, setPinOpen] = useState(false);
    const [token, setToken] = useState<string | null>(null);
    const [username, setUsername] = useState<string | null>(null);

    const [servers, setServers] = useState<RemoteServerVM[]>([]);
    const [selectedServer, setSelectedServer] = useState<RemoteServerVM | null>(null);
    const [libraries, setLibraries] = useState<RemoteLibraryVM[]>([]);
    const [selectedKeys, setSelectedKeys] = useState<Record<string, boolean>>({});
    const [includeWatch, setIncludeWatch] = useState(true);
    const [includeRatings, setIncludeRatings] = useState(true);

    const [job, setJob] = useState<LibraryMigrationJobVM | null>(null);
    const [busy, setBusy] = useState(false);
    const [loadingServers, setLoadingServers] = useState(false);
    const [loadingLibraries, setLoadingLibraries] = useState(false);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;
        libraryImportService
            .getProviders(serverId)
            .then(list => { if (!cancelled) setProvider(list.find(p => p.providerName === 'Plex') ?? list[0] ?? null); })
            .catch(() => { if (!cancelled) setProvider(null); });
        return () => { cancelled = true; };
    }, [serverId]);

    useSignalREvent<LibraryMigrationJobVM>('LibraryMigrationUpdated', useCallback((payload) => {
        setJob(prev => (prev && payload.jobId === prev.jobId ? payload : prev));
    }, []));

    const selectServer = useCallback(async (server: RemoteServerVM, accessToken: string) => {
        if (!provider) return;
        setSelectedServer(server);
        const connection = server.connections[0];
        if (!connection) {
            setError('That server has no usable connection.');
            return;
        }
        setLoadingLibraries(true);
        setError(null);
        try {
            const libs = await libraryImportService.listLibraries(provider.id, accessToken, connection.uri, serverId);
            setLibraries(libs);
            const defaults: Record<string, boolean> = {};
            for (const lib of libs) {
                if (lib.kind === 'Movie' || lib.kind === 'Show') defaults[lib.key] = true;
            }
            setSelectedKeys(defaults);
        } catch {
            setError('Could not load the libraries on that server.');
        } finally {
            setLoadingLibraries(false);
        }
    }, [provider, serverId]);

    const handleAuthorized = async (authorized: LibrarySyncTokenVM) => {
        setPinOpen(false);
        setToken(authorized.accessToken);
        setUsername(authorized.username ?? null);
        if (!provider) return;
        setLoadingServers(true);
        setError(null);
        try {
            const list = await libraryImportService.listServers(provider.id, authorized.accessToken, serverId);
            setServers(list);
            if (list.length === 1) {
                await selectServer(list[0], authorized.accessToken);
            }
        } catch {
            setError('Could not load your Plex servers.');
        } finally {
            setLoadingServers(false);
        }
    };

    const chosenKeys = useMemo(
        () => Object.entries(selectedKeys).filter(([, v]) => v).map(([k]) => k),
        [selectedKeys]
    );

    const jobRunning = job !== null && (job.state === 'Pending' || job.state === 'Running');
    const canRun = !!token
        && !!selectedServer
        && chosenKeys.length > 0
        && (includeWatch || includeRatings)
        && !busy
        && !jobRunning;

    const runImport = async () => {
        if (!provider || !selectedServer || !token) return;
        const connection = selectedServer.connections[0];
        if (!connection) {
            setError('That server has no usable connection.');
            return;
        }
        setBusy(true);
        setError(null);
        try {
            const created = await libraryImportService.runImport(provider.id, {
                accessToken: token,
                serverClientIdentifier: selectedServer.clientIdentifier,
                serverName: selectedServer.name,
                connectionUri: connection.uri,
                includeWatchState: includeWatch,
                includeRatings: includeRatings,
                librarySectionKeys: chosenKeys,
                plexUsername: username
            }, serverId);
            setJob(created);
        } catch {
            setError('Failed to start the import.');
        } finally {
            setBusy(false);
        }
    };

    const disconnect = () => {
        setToken(null);
        setUsername(null);
        setServers([]);
        setSelectedServer(null);
        setLibraries([]);
        setSelectedKeys({});
        setJob(null);
        setError(null);
    };

    const cardClass = 'rounded-[var(--vora-radius-lg)] p-6 space-y-4';
    const cardStyle = { background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)' } as const;
    const primaryButton = 'inline-flex items-center gap-2 rounded-[var(--vora-radius-md)] px-4 py-2 text-sm font-semibold cursor-pointer disabled:opacity-50 disabled:cursor-default';
    const primaryStyle = { background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)' } as const;
    const subtleButton = 'text-xs font-semibold cursor-pointer';

    if (provider === null) {
        return null;
    }

    const jobUser = job?.users?.[0];

    return (
        <section className={cardClass} style={cardStyle}>
            <div className="flex items-start justify-between gap-4">
                <div>
                    <h2 className="m-0 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Import from Plex</h2>
                    <p className="m-0 mt-1 text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                        Bring your watch history and ratings over from a Plex account into this profile. One-time import; nothing is stored after it finishes.
                    </p>
                </div>
                {token && (
                    <button type="button" onClick={disconnect} className={subtleButton} style={{ color: 'var(--vora-text-muted)' }}>
                        Start over
                    </button>
                )}
            </div>

            {!token && (
                <button type="button" onClick={() => setPinOpen(true)} className={primaryButton} style={primaryStyle}>
                    Connect Plex
                </button>
            )}

            {token && (
                <div className="space-y-4">
                    <div className="text-sm" style={{ color: 'var(--vora-text-secondary)' }}>
                        Connected as <span style={{ color: 'var(--vora-text-primary)' }}>{username ?? 'your Plex account'}</span>.
                    </div>

                    {loadingServers && <p className="text-sm" style={{ color: 'var(--vora-text-muted)' }}>Loading your Plex servers…</p>}

                    {!loadingServers && servers.length > 1 && (
                        <div>
                            <div className="mb-1.5 text-xs font-bold uppercase tracking-widest" style={{ color: 'var(--vora-text-muted)' }}>Server</div>
                            <div className="flex flex-wrap gap-2">
                                {servers.map(s => {
                                    const active = selectedServer?.clientIdentifier === s.clientIdentifier;
                                    return (
                                        <button
                                            key={s.clientIdentifier}
                                            type="button"
                                            onClick={() => token && selectServer(s, token)}
                                            className="rounded-full px-3 py-1.5 text-xs font-semibold cursor-pointer"
                                            style={{
                                                background: active ? 'var(--vora-accent-soft)' : 'rgba(255,255,255,0.04)',
                                                color: active ? 'var(--vora-accent-text)' : 'var(--vora-text-secondary)',
                                                border: `1px solid ${active ? 'var(--vora-accent-soft-hover)' : 'var(--vora-border-subtle)'}`
                                            }}
                                        >
                                            {s.name}
                                        </button>
                                    );
                                })}
                            </div>
                        </div>
                    )}

                    {selectedServer && (
                        <>
                            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                                <label className="flex items-center gap-2 text-sm" style={{ color: 'var(--vora-text-primary)' }}>
                                    <input type="checkbox" checked={includeWatch} onChange={e => setIncludeWatch(e.target.checked)} className="h-4 w-4 accent-[var(--vora-accent-500)]" />
                                    Watch history
                                </label>
                                <label className="flex items-center gap-2 text-sm" style={{ color: 'var(--vora-text-primary)' }}>
                                    <input type="checkbox" checked={includeRatings} onChange={e => setIncludeRatings(e.target.checked)} className="h-4 w-4 accent-[var(--vora-accent-500)]" />
                                    Ratings
                                </label>
                            </div>

                            <div>
                                <div className="mb-1.5 text-xs font-bold uppercase tracking-widest" style={{ color: 'var(--vora-text-muted)' }}>Libraries</div>
                                {loadingLibraries && <p className="text-sm" style={{ color: 'var(--vora-text-muted)' }}>Loading libraries…</p>}
                                {!loadingLibraries && libraries.length > 0 && (
                                    <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                                        {libraries.map(lib => {
                                            const supported = lib.kind === 'Movie' || lib.kind === 'Show';
                                            return (
                                                <label key={lib.key} className={'flex items-center gap-2 text-sm ' + (supported ? '' : 'opacity-50')} style={{ color: 'var(--vora-text-primary)' }}>
                                                    <input
                                                        type="checkbox"
                                                        checked={!!selectedKeys[lib.key]}
                                                        disabled={!supported}
                                                        onChange={e => setSelectedKeys(prev => ({ ...prev, [lib.key]: e.target.checked }))}
                                                        className="h-4 w-4 accent-[var(--vora-accent-500)]"
                                                    />
                                                    <span>{lib.name}</span>
                                                    <span className="rounded px-1.5 py-0.5 text-[10px] uppercase tracking-wide" style={{ background: 'var(--vora-bg-sunken)', color: 'var(--vora-text-muted)' }}>{lib.kind}</span>
                                                </label>
                                            );
                                        })}
                                    </div>
                                )}
                                {!loadingLibraries && libraries.some(l => l.kind !== 'Movie' && l.kind !== 'Show') && (
                                    <p className="mt-2 text-xs" style={{ color: 'var(--vora-text-muted)' }}>Only Movie and Show libraries can be imported.</p>
                                )}
                            </div>

                            <div className="flex items-center justify-between gap-3 pt-2" style={{ borderTop: '1px solid var(--vora-border-subtle)' }}>
                                <div className="text-xs" style={{ color: 'var(--vora-text-muted)' }}>
                                    {jobRunning && 'Importing… you can leave this page; it keeps running.'}
                                    {!jobRunning && chosenKeys.length === 0 && 'Pick at least one library.'}
                                    {!jobRunning && chosenKeys.length > 0 && !includeWatch && !includeRatings && 'Pick watch history or ratings.'}
                                </div>
                                <button type="button" onClick={runImport} disabled={!canRun} className={primaryButton} style={primaryStyle}>
                                    {busy ? 'Starting…' : job && (job.state === 'Completed' || job.state === 'Failed') ? 'Import again' : 'Import'}
                                </button>
                            </div>
                        </>
                    )}

                    {job && (
                        <div className="rounded-[var(--vora-radius-md)] p-3" style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}>
                            <div className="text-sm font-semibold" style={{ color: 'var(--vora-text-primary)' }}>
                                {job.state === 'Completed' && 'Import complete.'}
                                {job.state === 'Failed' && 'Import failed.'}
                                {(job.state === 'Pending' || job.state === 'Running') && 'Import in progress…'}
                            </div>
                            {jobUser && (
                                <div className="mt-1 text-xs" style={{ color: 'var(--vora-text-muted)' }}>
                                    Watch history: {jobUser.watchStatesImported} imported / {jobUser.watchStatesFetched} found
                                    {' · '}
                                    Ratings: {jobUser.ratingsImported} imported / {jobUser.ratingsFetched} found
                                    {jobUser.skipped > 0 && (' · ' + jobUser.skipped + ' skipped (not in your Vora library)')}
                                </div>
                            )}
                            {job.errorMessage && <p className="mt-1 text-xs" style={{ color: 'var(--vora-danger-text)' }}>{job.errorMessage}</p>}
                        </div>
                    )}
                </div>
            )}

            {error && <p className="text-sm" style={{ color: 'var(--vora-danger-text)' }}>{error}</p>}

            {provider && (
                <LibrarySyncPinModal
                    isOpen={pinOpen}
                    providerId={provider.id}
                    providerName={provider.providerName}
                    serverId={serverId}
                    createPin={libraryImportService.createPin}
                    pollPin={libraryImportService.pollPin}
                    onClose={() => setPinOpen(false)}
                    onAuthorized={handleAuthorized}
                />
            )}
        </section>
    );
}
