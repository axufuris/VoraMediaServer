import { useCallback, useEffect, useMemo, useState } from 'react';
import { LibrarySyncPinModal } from './LibrarySyncPinModal';
import {
    libraryImportService,
    type LibraryMigrationJobVM,
    type LibrarySyncProviderVM,
    type LibrarySyncTokenVM,
    type RemoteAccountVM,
    type RemoteLibraryVM,
    type RemoteServerVM
} from '../../api/LibraryMigration/libraryImportService';
import { userService } from '../../api/Users/userService';
import { useSignalREvent } from '../../hooks/useSignalREvent';
import { StorageKeys, getProfileIdFromToken } from '../../utils/storageKeys';
import { migrationSummary, sharedTitleNote } from '../../utils/migrationSummary';
import { errorDetail } from '../../utils/apiError';

interface PlexImportPanelProps {
    serverId?: string;
}

interface ProfileChoice {
    id: string;
    name: string;
}

const labelClass = 'mb-1.5 block text-xs font-bold uppercase tracking-widest';
const selectClass = 'w-full cursor-pointer rounded-md p-2.5 text-sm outline-none';
const selectStyle = { background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' } as const;

const accountLabel = (account: RemoteAccountVM) => account.isSignedIn ? `${account.displayName} (you)` : account.displayName;

export default function PlexImportPanel({ serverId }: PlexImportPanelProps) {
    const isAdmin = localStorage.getItem(StorageKeys.isServerAdmin) === 'true';
    const isProfileAdmin = localStorage.getItem(StorageKeys.isProfileAdmin) === 'true';
    const activeProfileId = getProfileIdFromToken(localStorage.getItem(StorageKeys.profileToken)) ?? '';

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
    const [setAsAdminRatings, setSetAsAdminRatings] = useState(false);

    const [accounts, setAccounts] = useState<RemoteAccountVM[]>([]);
    const [plexAccountId, setPlexAccountId] = useState('');
    const [plexPin, setPlexPin] = useState('');
    const [profiles, setProfiles] = useState<ProfileChoice[]>([]);
    const [targetProfileId, setTargetProfileId] = useState(activeProfileId);

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

    useEffect(() => {
        const userId = localStorage.getItem(StorageKeys.userId);
        if (!isProfileAdmin || !userId) return;
        let cancelled = false;
        userService.getUserAccount(userId, serverId)
            .then(account => { if (!cancelled) setProfiles(account.profiles.map(p => ({ id: p.id, name: p.name }))); })
            .catch(() => { if (!cancelled) setProfiles([]); });
        return () => { cancelled = true; };
    }, [isProfileAdmin, serverId]);

    useSignalREvent<LibraryMigrationJobVM>('LibraryMigrationUpdated', useCallback((payload) => {
        setJob(prev => (prev && payload.jobId === prev.jobId ? payload : prev));
    }, []));

    const selectServer = useCallback(async (server: RemoteServerVM, accessToken: string) => {
        if (!provider) return;
        setSelectedServer(server);
        setLoadingLibraries(true);
        setError(null);
        try {
            const libs = await libraryImportService.listLibraries(provider.id, accessToken, server.clientIdentifier, serverId);
            setLibraries(libs);
            const defaults: Record<string, boolean> = {};
            for (const lib of libs) {
                if (lib.kind === 'Movie' || lib.kind === 'Show') defaults[lib.key] = true;
            }
            setSelectedKeys(defaults);
        } catch (err) {
            setError(errorDetail(err, 'Could not load the libraries on that server'));
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
        libraryImportService.listAccounts(provider.id, authorized.accessToken, serverId)
            .then(list => {
                setAccounts(list);
                setPlexAccountId(list.find(a => a.isSignedIn)?.id ?? list[0]?.id ?? '');
            })
            .catch(() => setAccounts([]));
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

    const plexAccount = accounts.find(a => a.id === plexAccountId) ?? null;
    const importsSomeoneElse = plexAccount !== null && !plexAccount.isSignedIn;
    const needsPin = importsSomeoneElse && plexAccount.hasPin;
    const intoOwnProfile = targetProfileId === activeProfileId;
    const offersAdminRatings = isAdmin && includeRatings && intoOwnProfile && !importsSomeoneElse;
    const targetName = profiles.find(p => p.id === targetProfileId)?.name;

    const jobRunning = job !== null && (job.state === 'Pending' || job.state === 'Running');
    const canRun = !!token
        && !!selectedServer
        && chosenKeys.length > 0
        && (includeWatch || includeRatings)
        && (!needsPin || plexPin.trim().length > 0)
        && !busy
        && !jobRunning;

    const runImport = async () => {
        if (!provider || !selectedServer || !token) return;
        setBusy(true);
        setError(null);
        try {
            const created = await libraryImportService.runImport(provider.id, {
                accessToken: token,
                serverClientIdentifier: selectedServer.clientIdentifier,
                serverName: selectedServer.name,
                includeWatchState: includeWatch,
                includeRatings: includeRatings,
                librarySectionKeys: chosenKeys,
                plexUsername: username,
                setAdminRatings: offersAdminRatings && setAsAdminRatings,
                plexAccountId: importsSomeoneElse ? plexAccount.id : null,
                plexAccountName: importsSomeoneElse ? plexAccount.displayName : null,
                pin: needsPin ? plexPin.trim() : null,
                profileId: intoOwnProfile ? null : targetProfileId
            }, serverId);
            setJob(created);
        } catch (err) {
            setError(errorDetail(err, 'Failed to start the import'));
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
        setAccounts([]);
        setPlexAccountId('');
        setPlexPin('');
        setTargetProfileId(activeProfileId);
        setJob(null);
        setError(null);
    };

    const primaryButton = 'inline-flex items-center gap-2 rounded-[var(--vora-radius-md)] px-4 py-2 text-sm font-semibold cursor-pointer disabled:opacity-50 disabled:cursor-default';
    const primaryStyle = { background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)' } as const;
    const subtleButton = 'shrink-0 whitespace-nowrap text-xs font-semibold cursor-pointer';

    if (provider === null) {
        return null;
    }

    const jobUser = job?.users?.[0];

    return (
        <section className="vora-card p-6 space-y-4">
            <div className="flex items-start justify-between gap-4">
                <div>
                    <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Import from Plex</h2>
                    <p className="m-0 text-sm" style={{ color: 'var(--vora-text-secondary)' }}>
                        Bring watch history and ratings over from Plex. Sign in with your own Plex account, so the history is yours.
                        {isProfileAdmin && ' If you run a Plex Home, you can also bring over a Home user\'s history, a child\'s for example, into one of the profiles on your account.'}
                        {' '}Nothing is kept after the import finishes.
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
                            <div className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Server</div>
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
                                                background: active ? 'var(--vora-accent-soft)' : 'var(--vora-bg-sunken)',
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
                            {(accounts.length > 1 || profiles.length > 1) && (
                                <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                                    {accounts.length > 1 && (
                                        <div>
                                            <label htmlFor="plex-import-account" className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Whose Plex history</label>
                                            <select id="plex-import-account" value={plexAccountId} onChange={e => { setPlexAccountId(e.target.value); setPlexPin(''); }} className={selectClass} style={selectStyle}>
                                                {accounts.map(a => <option key={a.id} value={a.id}>{accountLabel(a)}</option>)}
                                            </select>
                                            {needsPin && (
                                                <input
                                                    type="password"
                                                    inputMode="numeric"
                                                    autoComplete="off"
                                                    value={plexPin}
                                                    onChange={e => setPlexPin(e.target.value)}
                                                    placeholder={`${plexAccount.displayName}'s Plex PIN`}
                                                    aria-label={`${plexAccount.displayName}'s Plex PIN`}
                                                    maxLength={8}
                                                    className="vora-input mt-2 w-full"
                                                />
                                            )}
                                        </div>
                                    )}
                                    {profiles.length > 1 && (
                                        <div>
                                            <label htmlFor="plex-import-profile" className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Import into</label>
                                            <select id="plex-import-profile" value={targetProfileId} onChange={e => setTargetProfileId(e.target.value)} className={selectClass} style={selectStyle}>
                                                {profiles.map(p => <option key={p.id} value={p.id}>{p.id === activeProfileId ? `${p.name} (this profile)` : p.name}</option>)}
                                            </select>
                                        </div>
                                    )}
                                </div>
                            )}

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

                            {offersAdminRatings && (
                                <label className="flex items-center gap-2 text-sm" style={{ color: 'var(--vora-text-primary)' }}>
                                    <input type="checkbox" checked={setAsAdminRatings} onChange={e => setSetAsAdminRatings(e.target.checked)} className="h-4 w-4 accent-[var(--vora-accent-500)]" />
                                    Also set these as the server Admin Rating
                                </label>
                            )}

                            <div>
                                <div className={labelClass} style={{ color: 'var(--vora-text-muted)' }}>Libraries</div>
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
                                    {!jobRunning && chosenKeys.length > 0 && (includeWatch || includeRatings) && needsPin && !plexPin.trim() && `Enter ${plexAccount.displayName}'s Plex PIN.`}
                                    {!jobRunning && canRun && (importsSomeoneElse || !intoOwnProfile) && `${importsSomeoneElse ? `${plexAccount.displayName}'s` : 'Your'} history goes into ${intoOwnProfile ? 'this profile' : targetName ?? 'the profile picked'}.`}
                                </div>
                                <button type="button" onClick={runImport} disabled={!canRun} className={primaryButton} style={primaryStyle}>
                                    {busy ? 'Starting…' : job && (job.state === 'Completed' || job.state === 'Failed') ? 'Import again' : 'Import'}
                                </button>
                            </div>
                        </>
                    )}

                    {job && (
                        <div className="rounded-[var(--vora-radius-md)] p-3" style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}>
                            <div role="status" className="text-sm font-semibold" style={{ color: 'var(--vora-text-primary)' }}>
                                {job.state === 'Completed' && 'Import complete.'}
                                {job.state === 'Failed' && 'Import failed.'}
                                {(job.state === 'Pending' || job.state === 'Running') && 'Import in progress…'}
                            </div>
                            {jobUser && (
                                <div className="mt-1 text-xs" style={{ color: 'var(--vora-text-muted)' }}>
                                    <div>{jobUser.accountName} → {jobUser.profileName}</div>
                                    {migrationSummary(jobUser, 'Watch history')}
                                    {sharedTitleNote(jobUser) && <div className="mt-0.5">{sharedTitleNote(jobUser)}</div>}
                                </div>
                            )}
                            {jobUser && jobUser.skippedSamples && jobUser.skippedSamples.length > 0 && (
                                <details className="mt-1">
                                    <summary className="cursor-pointer text-xs" style={{ color: 'var(--vora-text-muted)' }}>
                                        Show unmatched sample ({jobUser.skippedSamples.length} of {jobUser.skipped})
                                    </summary>
                                    <ul className="mt-1 space-y-0.5 font-mono text-[11px]" style={{ color: 'var(--vora-text-muted)' }}>
                                        {jobUser.skippedSamples.map((s, i) => <li key={i}>{s}</li>)}
                                    </ul>
                                </details>
                            )}
                            {jobUser?.errorMessage && <p className="mt-1 text-xs" style={{ color: 'var(--vora-danger-text)' }}>{jobUser.errorMessage}</p>}
                            {job.errorMessage && <p className="mt-1 text-xs" style={{ color: 'var(--vora-danger-text)' }}>{job.errorMessage}</p>}
                        </div>
                    )}
                </div>
            )}

            {error && <p className="text-sm" style={{ color: 'var(--vora-danger-text)' }}>{error}</p>}

            <LibrarySyncPinModal
                isOpen={pinOpen}
                providerId={provider.id}
                providerName={provider.providerName}
                serverId={serverId}
                onClose={() => setPinOpen(false)}
                onAuthorized={handleAuthorized}
            />
        </section>
    );
}
