import { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import { LibrarySyncPinModal } from '../../components/LibraryMigration/LibrarySyncPinModal';
import { useSignalREvent } from '../../hooks/useSignalREvent';
import {
    libraryMigrationService,
    type LibraryMigrationJobVM,
    type LibrarySyncProviderVM,
    type LibrarySyncTokenVM,
    type RemoteAccountVM,
    type RemoteLibraryVM,
    type RemoteServerVM,
    type RunLibraryMigrationMappingInput
} from '../../api/LibraryMigration/libraryMigrationService';
import { userService, type UserVM } from '../../api/Users/userService';

const JOB_STORAGE_KEY = 'vora_library_migration_job_id';

interface ConnectedState {
    provider: LibrarySyncProviderVM;
    accessToken: string;
    username: string | null;
}

interface ProfileOption {
    profileId: string;
    label: string;
}

interface ScopeState {
    includeWatchState: boolean;
    includeRatings: boolean;
}

const DEFAULT_SCOPE: ScopeState = {
    includeWatchState: true,
    includeRatings: true
};

export default function LibraryMigrationPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const [providers, setProviders] = useState<LibrarySyncProviderVM[]>([]);
    const [loadingProviders, setLoadingProviders] = useState(true);
    const [loadError, setLoadError] = useState<string | null>(null);
    const [activeProvider, setActiveProvider] = useState<LibrarySyncProviderVM | null>(null);
    const [connected, setConnected] = useState<ConnectedState | null>(null);

    const [servers, setServers] = useState<RemoteServerVM[]>([]);
    const [loadingServers, setLoadingServers] = useState(false);
    const [serversError, setServersError] = useState<string | null>(null);
    const [selectedServer, setSelectedServer] = useState<RemoteServerVM | null>(null);

    const [accounts, setAccounts] = useState<RemoteAccountVM[]>([]);
    const [loadingAccounts, setLoadingAccounts] = useState(false);
    const [accountsError, setAccountsError] = useState<string | null>(null);

    const [voraUsers, setVoraUsers] = useState<UserVM[]>([]);
    const [loadingVoraUsers, setLoadingVoraUsers] = useState(false);
    const [voraUsersError, setVoraUsersError] = useState<string | null>(null);

    const [libraries, setLibraries] = useState<RemoteLibraryVM[]>([]);
    const [loadingLibraries, setLoadingLibraries] = useState(false);
    const [librariesError, setLibrariesError] = useState<string | null>(null);
    const [selectedLibraryKeys, setSelectedLibraryKeys] = useState<Record<string, boolean>>({});

    const [mapping, setMapping] = useState<Record<string, string>>({});
    const [pins, setPins] = useState<Record<string, string>>({});
    const [scope, setScope] = useState<ScopeState>(DEFAULT_SCOPE);
    const [setAsAdminRatings, setSetAsAdminRatings] = useState(false);

    const [activeJob, setActiveJob] = useState<LibraryMigrationJobVM | null>(null);
    const [runError, setRunError] = useState<string | null>(null);
    const [runStarting, setRunStarting] = useState(false);
    const [restoringJob, setRestoringJob] = useState(false);

    useEffect(() => {
        const storedJobId = localStorage.getItem(JOB_STORAGE_KEY);
        if (!storedJobId) return;
        let cancelled = false;
        setRestoringJob(true);
        libraryMigrationService
            .getJob(storedJobId, serverId)
            .then(job => { if (!cancelled) setActiveJob(job); })
            .catch(() => {
                if (cancelled) return;
                localStorage.removeItem(JOB_STORAGE_KEY);
            })
            .finally(() => { if (!cancelled) setRestoringJob(false); });
        return () => { cancelled = true; };
    }, [serverId]);

    useEffect(() => {
        let cancelled = false;
        setLoadingProviders(true);
        setLoadError(null);
        libraryMigrationService
            .getProviders(serverId)
            .then(list => { if (!cancelled) setProviders(list); })
            .catch(err => { if (!cancelled) setLoadError(err instanceof Error ? err.message : 'Failed to load providers.'); })
            .finally(() => { if (!cancelled) setLoadingProviders(false); });
        return () => { cancelled = true; };
    }, [serverId]);

    useEffect(() => {
        if (!connected) return;
        let cancelled = false;
        setLoadingServers(true);
        setServersError(null);
        setServers([]);
        setSelectedServer(null);
        libraryMigrationService
            .listServers(connected.provider.id, connected.accessToken, serverId)
            .then(list => { if (!cancelled) setServers(list); })
            .catch(err => { if (!cancelled) setServersError(err instanceof Error ? err.message : 'Failed to load servers.'); })
            .finally(() => { if (!cancelled) setLoadingServers(false); });
        return () => { cancelled = true; };
    }, [connected, serverId]);

    useEffect(() => {
        if (!connected || !selectedServer) return;
        let cancelled = false;

        setLoadingAccounts(true);
        setAccountsError(null);
        setAccounts([]);
        setMapping({});
        setPins({});
        libraryMigrationService
            .listAccounts(connected.provider.id, connected.accessToken, serverId)
            .then(list => { if (!cancelled) setAccounts(list); })
            .catch(err => { if (!cancelled) setAccountsError(err instanceof Error ? err.message : 'Failed to load accounts.'); })
            .finally(() => { if (!cancelled) setLoadingAccounts(false); });

        setLoadingVoraUsers(true);
        setVoraUsersError(null);
        setVoraUsers([]);
        userService
            .getAllUsers(serverId)
            .then(list => { if (!cancelled) setVoraUsers(list); })
            .catch(err => { if (!cancelled) setVoraUsersError(err instanceof Error ? err.message : 'Failed to load Vora users.'); })
            .finally(() => { if (!cancelled) setLoadingVoraUsers(false); });

        const connection = selectedServer.connections[0];
        if (connection) {
            setLoadingLibraries(true);
            setLibrariesError(null);
            setLibraries([]);
            setSelectedLibraryKeys({});
            libraryMigrationService
                .listLibraries(connected.provider.id, connected.accessToken, connection.uri, serverId)
                .then(list => {
                    if (cancelled) return;
                    setLibraries(list);
                    const defaults: Record<string, boolean> = {};
                    for (const lib of list) {
                        if (lib.kind === 'Movie' || lib.kind === 'Show') defaults[lib.key] = true;
                    }
                    setSelectedLibraryKeys(defaults);
                })
                .catch(err => { if (!cancelled) setLibrariesError(err instanceof Error ? err.message : 'Failed to load libraries.'); })
                .finally(() => { if (!cancelled) setLoadingLibraries(false); });
        }

        return () => { cancelled = true; };
    }, [connected, selectedServer, serverId]);

    const handleJobEvent = useCallback((payload: LibraryMigrationJobVM) => {
        setActiveJob(prev => {
            if (!prev) return prev;
            if (payload.jobId !== prev.jobId) return prev;
            return payload;
        });
    }, []);
    useSignalREvent<LibraryMigrationJobVM>('LibraryMigrationUpdated', handleJobEvent);

    const handleAuthorized = (token: LibrarySyncTokenVM) => {
        if (!activeProvider) return;
        setConnected({
            provider: activeProvider,
            accessToken: token.accessToken,
            username: token.username ?? null
        });
    };

    const handleDisconnect = () => {
        setConnected(null);
        setServers([]);
        setSelectedServer(null);
        setServersError(null);
        setAccounts([]);
        setMapping({});
        setPins({});
        setLibraries([]);
        setSelectedLibraryKeys({});
        setActiveJob(null);
        setRunError(null);
        localStorage.removeItem(JOB_STORAGE_KEY);
    };

    const handleResetForNewRun = () => {
        setActiveJob(null);
        setRunError(null);
        localStorage.removeItem(JOB_STORAGE_KEY);
    };

    const profileOptions: ProfileOption[] = useMemo(() => {
        const result: ProfileOption[] = [];
        for (const user of voraUsers) {
            for (const profile of user.profiles) {
                const label = user.profiles.length > 1
                    ? user.displayName + ' / ' + profile.name
                    : profile.name + ' (' + user.displayName + ')';
                result.push({ profileId: profile.id, label });
            }
        }
        result.sort((a, b) => a.label.localeCompare(b.label));
        return result;
    }, [voraUsers]);

    const mappedAccounts = useMemo(() => {
        return accounts
            .map(a => ({ account: a, profileId: mapping[a.id] ?? '' }))
            .filter(p => p.profileId !== '');
    }, [accounts, mapping]);

    const missingPins = useMemo(() => {
        return mappedAccounts.filter(p => p.account.hasPin && !pins[p.account.id]);
    }, [mappedAccounts, pins]);

    const chosenLibraryKeys = useMemo(
        () => Object.entries(selectedLibraryKeys).filter(([, v]) => v).map(([k]) => k),
        [selectedLibraryKeys]
    );

    const scopeValid = scope.includeWatchState || scope.includeRatings;
    const librariesValid = chosenLibraryKeys.length > 0;
    const canRun = mappedAccounts.length > 0 && scopeValid && librariesValid && missingPins.length === 0 && !runStarting && !activeJob;

    const handleRun = async () => {
        if (!connected || !selectedServer) return;
        setRunStarting(true);
        setRunError(null);

        const connection = selectedServer.connections[0];
        if (!connection) {
            setRunError('The selected server has no usable connection.');
            setRunStarting(false);
            return;
        }

        const profileLabelById: Record<string, string> = {};
        for (const opt of profileOptions) profileLabelById[opt.profileId] = opt.label;

        const inputs: RunLibraryMigrationMappingInput[] = mappedAccounts.map(p => ({
            accountId: p.account.id,
            accountName: p.account.displayName,
            profileId: p.profileId,
            profileName: profileLabelById[p.profileId] ?? p.profileId,
            pin: p.account.hasPin ? pins[p.account.id] : null
        }));

        try {
            const job = await libraryMigrationService.runMigration(connected.provider.id, {
                accessToken: connected.accessToken,
                serverClientIdentifier: selectedServer.clientIdentifier,
                serverName: selectedServer.name,
                connectionUri: connection.uri,
                includeWatchState: scope.includeWatchState,
                includeRatings: scope.includeRatings,
                librarySectionKeys: chosenLibraryKeys,
                mappings: inputs,
                setAdminRatings: scope.includeRatings && setAsAdminRatings
            }, serverId);
            setActiveJob(job);
            localStorage.setItem(JOB_STORAGE_KEY, job.jobId);
        } catch (err) {
            setRunError(err instanceof Error ? err.message : 'Failed to start the migration.');
        } finally {
            setRunStarting(false);
        }
    };

    const subtitle = useMemo(() => 'One-time import of watch state and ratings from another media server.', []);

    const accentBorder = 'border-l-4 border-[var(--vora-accent-500)]';
    const onlineBadge = 'text-[10px] uppercase tracking-wide px-1.5 py-0.5 rounded bg-green-900/50 text-green-300';
    const offlineBadge = 'text-[10px] uppercase tracking-wide px-1.5 py-0.5 rounded bg-[var(--vora-bg-sunken)] text-[var(--vora-text-muted)]';
    const ownerKindBadge = 'text-[10px] uppercase tracking-wide px-1.5 py-0.5 rounded bg-[var(--vora-accent-500)]/30 text-[var(--vora-accent-text)]';
    const homeKindBadge = 'text-[10px] uppercase tracking-wide px-1.5 py-0.5 rounded bg-blue-900/50 text-blue-300';
    const pinBadge = 'text-[10px] uppercase tracking-wide px-1.5 py-0.5 rounded bg-yellow-900/50 text-yellow-300';
    const libraryKindBadge = 'text-[10px] uppercase tracking-wide px-1.5 py-0.5 rounded bg-[var(--vora-bg-sunken)] text-[var(--vora-text-muted)]';
    const primaryButton = 'px-4 py-2 text-sm rounded-md bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)] hover:opacity-90 disabled:opacity-50 disabled:cursor-default';
    const secondaryButton = 'px-3 py-1.5 text-xs text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)]';
    const inputClass = 'bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded-md text-sm px-2 py-1.5 text-[var(--vora-text-primary)]';

    const renderKindBadge = (kind: string) => {
        if (kind === 'Owner') return <span className={ownerKindBadge}>Owner</span>;
        if (kind === 'Home') return <span className={homeKindBadge}>Home</span>;
        return <span className={homeKindBadge}>{kind}</span>;
    };

    const renderUserStateBadge = (state: string) => {
        const base = 'text-[10px] uppercase tracking-wide px-1.5 py-0.5 rounded';
        switch (state) {
            case 'Running': return <span className={base + ' bg-blue-900/50 text-blue-300'}>Running</span>;
            case 'Completed': return <span className={base + ' bg-green-900/50 text-green-300'}>Completed</span>;
            case 'Failed': return <span className={base + ' bg-[var(--vora-danger-soft)]/50 text-[var(--vora-danger-500)]'}>Failed</span>;
            case 'Skipped': return <span className={base + ' bg-[var(--vora-bg-sunken)] text-[var(--vora-text-muted)]'}>Skipped</span>;
            default: return <span className={base + ' bg-[var(--vora-bg-sunken)] text-[var(--vora-text-muted)]'}>Pending</span>;
        }
    };

    const renderJobView = (job: LibraryMigrationJobVM) => (
        <div className="vora-card p-4 space-y-3">
            <div className="flex items-center justify-between">
                <div className="text-sm font-semibold text-[var(--vora-text-primary)]">
                    Migration {job.state.toLowerCase()}
                    {job.serverName ? ' — ' + job.serverName : ''}
                </div>
                {(job.state === 'Completed' || job.state === 'Failed') && (
                    <button type="button" onClick={handleResetForNewRun} className={secondaryButton}>Run again</button>
                )}
            </div>
            {job.errorMessage && <p className="text-sm text-[var(--vora-danger-500)]">{job.errorMessage}</p>}
            {job.users.map(user => (
                <div key={user.accountId} className="border-t border-[var(--vora-border-subtle)] pt-2">
                    <div className="flex items-center justify-between">
                        <div className="text-sm text-[var(--vora-text-primary)] flex items-center gap-2">
                            <span>{user.accountName} <span className="text-[var(--vora-text-muted)]">→</span> {user.profileName}</span>
                            {renderUserStateBadge(user.state)}
                        </div>
                    </div>
                    <div className="text-xs text-[var(--vora-text-muted)] mt-1">
                        Watch states: {user.watchStatesImported} imported / {user.watchStatesFetched} fetched
                        {' · '}
                        Ratings: {user.ratingsImported} imported / {user.ratingsFetched} fetched
                        {user.skipped > 0 && (' · ' + user.skipped + ' skipped (no match)')}
                    </div>
                    {user.skippedSamples && user.skippedSamples.length > 0 && (
                        <details className="mt-1">
                            <summary className="text-xs cursor-pointer text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)]">
                                Show unmatched sample ({user.skippedSamples.length} of {user.skipped})
                            </summary>
                            <ul className="mt-1 space-y-0.5 font-mono text-[11px] text-[var(--vora-text-muted)]">
                                {user.skippedSamples.map((s, i) => <li key={i}>{s}</li>)}
                            </ul>
                        </details>
                    )}
                    {user.errorMessage && <p className="text-xs text-[var(--vora-danger-500)] mt-1">{user.errorMessage}</p>}
                </div>
            ))}
        </div>
    );

    return (
        <div data-vora-page="">
            <PageHeader title="Library Migration" description={subtitle} />

            <div className="px-8 pt-6 pb-10 max-w-4xl mx-auto space-y-4">
                {restoringJob && !activeJob && (
                    <p className="text-sm text-[var(--vora-text-muted)]">Restoring previous migration...</p>
                )}

                {activeJob && (
                    <>
                        <div className={'vora-card p-4 ' + accentBorder}>
                            <div className="text-sm text-[var(--vora-text-primary)]">
                                {activeJob.state === 'Running' && 'Migration in progress. You can leave this page; results will keep updating.'}
                                {activeJob.state === 'Completed' && 'Migration complete.'}
                                {activeJob.state === 'Failed' && 'Migration failed. See details below.'}
                                {activeJob.state === 'Pending' && 'Migration starting...'}
                            </div>
                        </div>
                        {renderJobView(activeJob)}
                    </>
                )}

                {!activeJob && !restoringJob && (
                    <>
                        {loadingProviders && <p className="text-sm text-[var(--vora-text-muted)]">Loading available migration sources...</p>}
                        {loadError && <p className="text-sm text-[var(--vora-danger-500)]">{loadError}</p>}
                        {!loadingProviders && !loadError && providers.length === 0 && (
                            <p className="text-sm text-[var(--vora-text-muted)]">No library sync providers are installed.</p>
                        )}

                        {!connected && providers.map(provider => (
                            <div key={provider.id} className="vora-card p-4 flex items-center justify-between">
                                <div>
                                    <div className="font-semibold text-[var(--vora-text-primary)]">{provider.name}</div>
                                    <div className="text-xs text-[var(--vora-text-muted)]">{provider.description}</div>
                                </div>
                                <button type="button" onClick={() => setActiveProvider(provider)} className={primaryButton}>Connect</button>
                            </div>
                        ))}

                        {connected && (
                            <>
                                <div className={'vora-card p-4 flex items-center justify-between ' + accentBorder}>
                                    <div>
                                        <div className="text-sm text-[var(--vora-text-primary)]">
                                            Connected to {connected.provider.providerName}{connected.username ? ' as ' + connected.username : ''}.
                                        </div>
                                        <div className="text-xs text-[var(--vora-text-muted)] mt-1">
                                            {!selectedServer && 'Pick the server whose watch state and ratings you want to import.'}
                                            {selectedServer && 'Map users, pick what and where to import, then run.'}
                                        </div>
                                    </div>
                                    <button type="button" onClick={handleDisconnect} className={secondaryButton}>Start over</button>
                                </div>

                                {!selectedServer && (
                                    <>
                                        {loadingServers && <p className="text-sm text-[var(--vora-text-muted)]">Loading servers...</p>}
                                        {serversError && <p className="text-sm text-[var(--vora-danger-500)]">{serversError}</p>}
                                        {!loadingServers && !serversError && servers.length === 0 && (
                                            <p className="text-sm text-[var(--vora-text-muted)]">No servers were found on this account.</p>
                                        )}
                                        {servers.map(server => {
                                            const ownerLine = server.isOwned ? 'Owned by you' : 'Shared by ' + (server.ownerName ?? 'another user');
                                            const platformLine = server.platform ? ' · ' + server.platform : '';
                                            const versionLine = server.productVersion ? ' · v' + server.productVersion : '';
                                            const connCount = server.connections.length;
                                            const connLine = ' · ' + connCount + ' connection' + (connCount === 1 ? '' : 's');
                                            return (
                                                <div key={server.clientIdentifier} className="vora-card p-4 flex items-center justify-between">
                                                    <div className="flex-1">
                                                        <div className="font-semibold text-[var(--vora-text-primary)] flex items-center gap-2">
                                                            <span>{server.name}</span>
                                                            <span className={server.isOnline ? onlineBadge : offlineBadge}>{server.isOnline ? 'Online' : 'Offline'}</span>
                                                        </div>
                                                        <div className="text-xs text-[var(--vora-text-muted)] mt-1">{ownerLine}{platformLine}{versionLine}{connLine}</div>
                                                    </div>
                                                    <button type="button" onClick={() => setSelectedServer(server)} className={primaryButton}>Use this server</button>
                                                </div>
                                            );
                                        })}
                                    </>
                                )}

                                {selectedServer && (
                                    <>
                                        <div className="vora-card p-4 flex items-center justify-between">
                                            <div>
                                                <div className="text-sm text-[var(--vora-text-primary)]">Server: {selectedServer.name}</div>
                                                <div className="text-xs text-[var(--vora-text-muted)] mt-1">
                                                    {selectedServer.isOwned ? 'Owned by you' : 'Shared by ' + (selectedServer.ownerName ?? 'another user')}
                                                </div>
                                            </div>
                                            <button type="button" onClick={() => { setSelectedServer(null); setAccounts([]); setMapping({}); setPins({}); setLibraries([]); setSelectedLibraryKeys({}); }} className={secondaryButton}>Change server</button>
                                        </div>

                                        {(loadingAccounts || loadingVoraUsers) && <p className="text-sm text-[var(--vora-text-muted)]">Loading users...</p>}
                                        {accountsError && <p className="text-sm text-[var(--vora-danger-500)]">{accountsError}</p>}
                                        {voraUsersError && <p className="text-sm text-[var(--vora-danger-500)]">{voraUsersError}</p>}

                                        {!loadingAccounts && !loadingVoraUsers && !accountsError && !voraUsersError && accounts.length === 0 && (
                                            <p className="text-sm text-[var(--vora-text-muted)]">No {connected.provider.providerName} users were found.</p>
                                        )}

                                        {!loadingAccounts && !loadingVoraUsers && accounts.length > 0 && (
                                            <>
                                                {accounts.map(account => {
                                                    const isMapped = (mapping[account.id] ?? '') !== '';
                                                    return (
                                                        <div key={account.id} className="vora-card p-4 flex items-center gap-4">
                                                            {account.avatarUrl && (
                                                                <img src={account.avatarUrl} alt="" className="w-10 h-10 rounded-full object-cover bg-[var(--vora-bg-sunken)]" referrerPolicy="no-referrer" />
                                                            )}
                                                            <div className="flex-1">
                                                                <div className="font-semibold text-[var(--vora-text-primary)] flex items-center gap-2">
                                                                    <span>{account.displayName}</span>
                                                                    {renderKindBadge(account.kind)}
                                                                    {account.hasPin && <span className={pinBadge}>PIN required</span>}
                                                                </div>
                                                                {account.email && <div className="text-xs text-[var(--vora-text-muted)] mt-1">{account.email}</div>}
                                                                {isMapped && account.hasPin && (
                                                                    <div className="mt-2">
                                                                        <input
                                                                            type="password"
                                                                            inputMode="numeric"
                                                                            placeholder="Enter Plex PIN"
                                                                            value={pins[account.id] ?? ''}
                                                                            onChange={e => setPins(prev => ({ ...prev, [account.id]: e.target.value }))}
                                                                            className={inputClass + ' w-40'}
                                                                        />
                                                                    </div>
                                                                )}
                                                            </div>
                                                            <select
                                                                value={mapping[account.id] ?? ''}
                                                                onChange={e => setMapping(prev => ({ ...prev, [account.id]: e.target.value }))}
                                                                className={inputClass}
                                                            >
                                                                <option value="">Skip this user</option>
                                                                {profileOptions.map(opt => (
                                                                    <option key={opt.profileId} value={opt.profileId}>{opt.label}</option>
                                                                ))}
                                                            </select>
                                                        </div>
                                                    );
                                                })}

                                                <div className="vora-card p-4 space-y-4">
                                                    <div>
                                                        <div className="text-sm font-semibold text-[var(--vora-text-primary)] mb-2">Direction</div>
                                                        <label className="flex items-center gap-2 text-sm text-[var(--vora-text-primary)] mb-1">
                                                            <input type="radio" name="direction" checked readOnly />
                                                            Pull from {connected.provider.providerName} into Vora
                                                        </label>
                                                        <label className="flex items-center gap-2 text-sm text-[var(--vora-text-muted)]">
                                                            <input type="radio" name="direction" disabled />
                                                            Push to {connected.provider.providerName} <span className="text-xs">(coming soon)</span>
                                                        </label>
                                                    </div>

                                                    <div>
                                                        <div className="text-sm font-semibold text-[var(--vora-text-primary)] mb-2">What to import</div>
                                                        <div className="grid grid-cols-2 gap-2 text-sm text-[var(--vora-text-primary)]">
                                                            <label className="flex items-center gap-2">
                                                                <input type="checkbox" checked={scope.includeWatchState} onChange={e => setScope(s => ({ ...s, includeWatchState: e.target.checked }))} />
                                                                Watch state
                                                            </label>
                                                            <label className="flex items-center gap-2">
                                                                <input type="checkbox" checked={scope.includeRatings} onChange={e => setScope(s => ({ ...s, includeRatings: e.target.checked }))} />
                                                                Ratings
                                                            </label>
                                                        </div>
                                                        {scope.includeRatings && (
                                                            <label className="flex items-center gap-2 mt-2 text-sm text-[var(--vora-text-primary)]">
                                                                <input type="checkbox" checked={setAsAdminRatings} onChange={e => setSetAsAdminRatings(e.target.checked)} />
                                                                Also set these as the server Admin Rating
                                                            </label>
                                                        )}
                                                    </div>

                                                    <div>
                                                        <div className="text-sm font-semibold text-[var(--vora-text-primary)] mb-2">{connected.provider.providerName} libraries to import from</div>
                                                        {loadingLibraries && <p className="text-xs text-[var(--vora-text-muted)]">Loading libraries...</p>}
                                                        {librariesError && <p className="text-xs text-[var(--vora-danger-500)]">{librariesError}</p>}
                                                        {!loadingLibraries && !librariesError && libraries.length === 0 && (
                                                            <p className="text-xs text-[var(--vora-text-muted)]">No libraries were found on this server.</p>
                                                        )}
                                                        {libraries.length > 0 && (
                                                            <div className="grid grid-cols-2 gap-2 text-sm text-[var(--vora-text-primary)]">
                                                                {libraries.map(lib => {
                                                                    const isSupported = lib.kind === 'Movie' || lib.kind === 'Show';
                                                                    return (
                                                                        <label key={lib.key} className={'flex items-center gap-2 ' + (isSupported ? '' : 'opacity-50')}>
                                                                            <input
                                                                                type="checkbox"
                                                                                checked={!!selectedLibraryKeys[lib.key]}
                                                                                disabled={!isSupported}
                                                                                onChange={e => setSelectedLibraryKeys(prev => ({ ...prev, [lib.key]: e.target.checked }))}
                                                                            />
                                                                            <span>{lib.name}</span>
                                                                            <span className={libraryKindBadge}>{lib.kind}</span>
                                                                        </label>
                                                                    );
                                                                })}
                                                            </div>
                                                        )}
                                                        {libraries.some(l => l.kind !== 'Movie' && l.kind !== 'Show') && (
                                                            <p className="text-xs text-[var(--vora-text-muted)] mt-2">Only Movie and Show libraries are supported for now.</p>
                                                        )}
                                                    </div>

                                                    <div className="flex items-center justify-between pt-2 border-t border-[var(--vora-border-subtle)]">
                                                        <div className="text-xs text-[var(--vora-text-muted)]">
                                                            {mappedAccounts.length === 0 && 'Map at least one user to run.'}
                                                            {mappedAccounts.length > 0 && missingPins.length > 0 && (missingPins.length + ' user' + (missingPins.length === 1 ? '' : 's') + ' need a PIN.')}
                                                            {mappedAccounts.length > 0 && missingPins.length === 0 && !scopeValid && 'Pick at least Watch state or Ratings.'}
                                                            {mappedAccounts.length > 0 && missingPins.length === 0 && scopeValid && !librariesValid && 'Pick at least one library.'}
                                                            {canRun && (mappedAccounts.length + ' user' + (mappedAccounts.length === 1 ? '' : 's') + ' · ' + chosenLibraryKeys.length + ' librar' + (chosenLibraryKeys.length === 1 ? 'y' : 'ies') + ' ready to migrate.')}
                                                        </div>
                                                        <button type="button" onClick={handleRun} disabled={!canRun} className={primaryButton}>
                                                            {runStarting ? 'Starting...' : 'Run migration'}
                                                        </button>
                                                    </div>

                                                    {runError && <p className="text-sm text-[var(--vora-danger-500)]">{runError}</p>}
                                                </div>
                                            </>
                                        )}
                                    </>
                                )}
                            </>
                        )}
                    </>
                )}
            </div>

            {activeProvider && (
                <LibrarySyncPinModal
                    isOpen={!!activeProvider}
                    providerId={activeProvider.id}
                    providerName={activeProvider.providerName}
                    serverId={serverId}
                    onClose={() => setActiveProvider(null)}
                    onAuthorized={handleAuthorized}
                />
            )}
        </div>
    );
}
