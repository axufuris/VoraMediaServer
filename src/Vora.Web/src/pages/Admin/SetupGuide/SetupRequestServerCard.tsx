import { useCallback, useState } from 'react';
import { Link } from 'react-router-dom';
import { isAxiosError } from 'axios';
import { requestAdminService, type ProviderOptionDto, type RequestServerVM } from '../../../api/Discovery/requestAdminService';
import HealthBadge from '../../../components/Admin/Primitives/HealthBadge';
import { resolveAdminPath } from '../../../components/Admin/Shell/adminNavData';
import { useStepSaver } from './setupSaver';
import { CardHeading, CheckRow, FieldLabel, Hint, SetupCard, StatusMessage } from './SetupParts';
import { MINIMUM_AVAILABILITY_HINT, SEARCH_ON_ADD_HINT, RELEASE_CALENDAR_HINT } from '../../../components/Admin/Settings/requestServerText';

type Kind = 'radarr' | 'sonarr';

interface SetupRequestServerCardProps {
    kind: Kind;
    existing?: RequestServerVM;
    serverId?: string;
    onSaved: () => void;
}

interface ProviderSettings {
    qualityProfileId?: number;
    rootFolderPath?: string;
    minimumAvailability?: string;
    searchOnAdd: boolean;
}

const LABEL: Record<Kind, { name: string; what: string; port: number; provider: string; mediaType: string }> = {
    radarr: { name: 'Radarr', what: 'movies', port: 7878, provider: 'radarr_requester', mediaType: 'Movie' },
    sonarr: { name: 'Sonarr', what: 'shows', port: 8989, provider: 'sonarr_requester', mediaType: 'TvShow' },
};

const errorMessage = (err: unknown, fallback: string) => {
    if (isAxiosError(err)) {
        const data: { message?: string } | undefined = err.response?.data;
        return data?.message || fallback;
    }
    return err instanceof Error ? err.message : fallback;
};

export default function SetupRequestServerCard({ kind, existing, serverId, onSaved }: SetupRequestServerCardProps) {
    const meta = LABEL[kind];
    const [server, setServer] = useState<RequestServerVM>({
        name: meta.name, providerId: meta.provider, mediaType: meta.mediaType, hostname: '', port: meta.port, useSsl: false,
        apiKey: '', urlBase: '', isDefault: true, is4K: false, isEnabled: true, providesReleaseCalendar: true, providerSettingsJson: '{}',
    });
    const [settings, setSettings] = useState<ProviderSettings>({ minimumAvailability: kind === 'radarr' ? 'released' : undefined, searchOnAdd: true });
    const [profiles, setProfiles] = useState<ProviderOptionDto[]>([]);
    const [folders, setFolders] = useState<ProviderOptionDto[]>([]);
    const [state, setState] = useState<'idle' | 'testing' | 'connected' | 'failed' | 'saving'>('idle');
    const [message, setMessage] = useState<string | null>(null);

    const connected = state === 'connected' || state === 'saving';

    const update = (patch: Partial<RequestServerVM>) => {
        setServer(s => ({ ...s, ...patch }));
        if (connected) setState('idle');
        setMessage(null);
    };

    const test = async () => {
        if (!server.hostname.trim() || !server.apiKey.trim()) {
            setState('failed');
            setMessage('Enter the address and the API key first.');
            return;
        }
        setState('testing');
        setMessage(null);
        try {
            const base = { providerId: server.providerId, hostname: server.hostname.trim(), port: server.port, useSsl: server.useSsl, apiKey: server.apiKey.trim(), urlBase: server.urlBase };
            const loadedProfiles = await requestAdminService.getProviderOptions({ ...base, optionType: 'qualityProfiles' }, serverId);
            const loadedFolders = await requestAdminService.getProviderOptions({ ...base, optionType: 'rootFolders' }, serverId);
            setProfiles(loadedProfiles);
            setFolders(loadedFolders);
            setSettings(s => ({
                ...s,
                qualityProfileId: loadedProfiles.some(p => parseInt(p.id, 10) === s.qualityProfileId) ? s.qualityProfileId : loadedProfiles[0] ? parseInt(loadedProfiles[0].id, 10) : undefined,
                rootFolderPath: loadedFolders.some(f => f.name === s.rootFolderPath) ? s.rootFolderPath : loadedFolders[0]?.name,
            }));
            setState('connected');
            setMessage(`Connected to ${meta.name}. Choose where requests go below, then save.`);
        } catch (err) {
            setState('failed');
            setMessage(errorMessage(err, `Couldn't connect to ${meta.name}. Check the address, port and API key.`));
        }
    };

    const save = useCallback(async () => {
        setState('saving');
        try {
            await requestAdminService.saveServer({ ...server, hostname: server.hostname.trim(), apiKey: server.apiKey.trim(), providerSettingsJson: JSON.stringify(settings) }, serverId);
            onSaved();
        } catch (err) {
            setState('connected');
            setMessage(errorMessage(err, `Couldn't save ${meta.name}. Try again.`));
            throw err;
        }
    }, [server, settings, serverId, onSaved, meta.name]);

    useStepSaver(useCallback(async () => {
        if (state === 'connected' && !existing) await save();
    }, [state, existing, save]));

    if (existing) {
        return (
            <SetupCard>
                <CardHeading
                    title={meta.name}
                    subtitle={`${existing.useSsl ? 'https://' : 'http://'}${existing.hostname}:${existing.port}`}
                    badges={<>
                        {existing.providesReleaseCalendar && <HealthBadge tone="info" showDot={false}>Release Calendar</HealthBadge>}
                        <HealthBadge tone={existing.isEnabled ? 'ok' : 'neutral'}>{existing.isEnabled ? 'Connected' : 'Off'}</HealthBadge>
                    </>}
                />
                <p className="text-[13px] text-[var(--vora-text-secondary)]">
                    {meta.name} is already set up. Change it under <Link to={resolveAdminPath('/admin/settings', serverId)} className="font-semibold text-[var(--vora-accent-text)] hover:underline">System Settings → Request Servers</Link>.
                </p>
            </SetupCard>
        );
    }

    const id = (field: string) => `setup-${kind}-${field}`;

    return (
        <SetupCard>
            <CardHeading
                title={meta.name}
                subtitle={`Sends requested ${meta.what} to ${meta.name} to download`}
                badges={state === 'connected' || state === 'saving'
                    ? <HealthBadge tone="ok">Connected</HealthBadge>
                    : state === 'failed' ? <HealthBadge tone="error">Couldn't connect</HealthBadge>
                        : <HealthBadge tone="neutral" showDot={false}>Not connected</HealthBadge>}
            />
            <div className="flex flex-wrap gap-3">
                <div className="min-w-[200px] flex-1">
                    <FieldLabel htmlFor={id('host')}>Address</FieldLabel>
                    <input id={id('host')} className="vora-input font-mono text-sm" value={server.hostname} onChange={e => update({ hostname: e.target.value })} placeholder="192.168.1.20" autoComplete="off" />
                </div>
                <div className="w-28">
                    <FieldLabel htmlFor={id('port')}>Port</FieldLabel>
                    <input id={id('port')} type="number" className="vora-input font-mono text-sm" value={server.port} onChange={e => update({ port: parseInt(e.target.value, 10) || 0 })} />
                </div>
            </div>
            <div className="flex flex-wrap gap-3">
                <div className="min-w-[200px] flex-1">
                    <FieldLabel htmlFor={id('key')}>API key</FieldLabel>
                    <input id={id('key')} type="password" className="vora-input font-mono text-sm" value={server.apiKey} onChange={e => update({ apiKey: e.target.value })} placeholder={`Settings → General in ${meta.name}`} autoComplete="off" />
                </div>
                <div className="w-40">
                    <FieldLabel htmlFor={id('base')}>URL base</FieldLabel>
                    <input id={id('base')} className="vora-input font-mono text-sm" value={server.urlBase} onChange={e => update({ urlBase: e.target.value })} placeholder={`/${kind}`} autoComplete="off" />
                </div>
            </div>
            <CheckRow id={id('ssl')} checked={server.useSsl} onChange={useSsl => update({ useSsl })} label="Use HTTPS" />
            <CheckRow
                id={id('calendar')}
                checked={server.providesReleaseCalendar}
                onChange={providesReleaseCalendar => setServer(s => ({ ...s, providesReleaseCalendar }))}
                label="Use for the Release Calendar"
                hint={RELEASE_CALENDAR_HINT(meta.name, meta.what)}
            />

            {connected && (
                <div className="flex flex-col gap-4 border-t border-dashed border-[var(--vora-border-strong)] pt-4">
                    <div className="flex flex-wrap gap-3">
                        <div className="min-w-[200px] flex-1">
                            <FieldLabel htmlFor={id('profile')}>Quality profile</FieldLabel>
                            <select id={id('profile')} className="vora-input cursor-pointer" value={settings.qualityProfileId ?? ''} onChange={e => setSettings(s => ({ ...s, qualityProfileId: parseInt(e.target.value, 10) }))}>
                                {profiles.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                            </select>
                            <Hint>The quality {meta.name} looks for, from the profiles set up in {meta.name}.</Hint>
                        </div>
                        <div className="min-w-[200px] flex-1">
                            <FieldLabel htmlFor={id('folder')}>Root folder</FieldLabel>
                            <select id={id('folder')} className="vora-input cursor-pointer" value={settings.rootFolderPath ?? ''} onChange={e => setSettings(s => ({ ...s, rootFolderPath: e.target.value }))}>
                                {folders.map(f => <option key={f.id} value={f.name}>{f.name}</option>)}
                            </select>
                            <Hint>Where {meta.name} saves requested {meta.what}. Use a folder a Vora library watches, so they appear here when done.</Hint>
                        </div>
                    </div>
                    {kind === 'radarr' && (
                        <div>
                            <FieldLabel htmlFor={id('availability')}>Minimum availability</FieldLabel>
                            <select id={id('availability')} className="vora-input max-w-sm cursor-pointer" value={settings.minimumAvailability ?? 'released'} onChange={e => setSettings(s => ({ ...s, minimumAvailability: e.target.value }))}>
                                <option value="announced">Announced</option>
                                <option value="inCinemas">In cinemas</option>
                                <option value="released">Released</option>
                            </select>
                            <Hint>{MINIMUM_AVAILABILITY_HINT}</Hint>
                        </div>
                    )}
                    <CheckRow id={id('search')} checked={settings.searchOnAdd} onChange={searchOnAdd => setSettings(s => ({ ...s, searchOnAdd }))} label="Search automatically when a request comes in" hint={SEARCH_ON_ADD_HINT(meta.name)} />
                </div>
            )}

            <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="min-w-0 flex-1">
                    {message && <StatusMessage tone={state === 'failed' ? 'error' : 'ok'}>{message}</StatusMessage>}
                </div>
                <div className="flex items-center gap-2">
                    <button type="button" onClick={test} disabled={state === 'testing' || state === 'saving'} className={`${connected ? 'vora-button-secondary' : 'vora-button-primary'} !px-3 !py-1.5 text-xs`}>
                        {state === 'testing' ? 'Connecting…' : connected ? 'Test again' : 'Test connection'}
                    </button>
                    {connected && (
                        <button type="button" onClick={() => { void save().catch(() => { }); }} disabled={state === 'saving'} className="vora-button-primary !px-3 !py-1.5 text-xs">
                            {state === 'saving' ? 'Saving…' : `Save ${meta.name}`}
                        </button>
                    )}
                </div>
            </div>
        </SetupCard>
    );
}
