import { useEffect, useState, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { requestAdminService, type MediaRequestVM, type RequestServerVM, type ProviderOptionDto } from '../../api/Discovery/requestAdminService';
import { useDialog } from '../../dialogs';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import HealthBadge from '../../components/Admin/Primitives/HealthBadge';
import EmptyState from '../../components/Admin/Primitives/EmptyState';

export default function RequestsPage() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const navigate = useNavigate();
    const [requests, setRequests] = useState<MediaRequestVM[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [activeStatus, setActiveStatus] = useState<number>(0);

    const [servers, setServers] = useState<RequestServerVM[]>([]);
    const [profilesMap, setProfilesMap] = useState<Record<string, ProviderOptionDto[]>>({});
    const [selectedProfiles, setSelectedProfiles] = useState<Record<string, number>>({});

    const loadData = useCallback(async () => {
        setIsLoading(true);
        try {
            const [reqs, srvs] = await Promise.all([
                requestAdminService.getRequests(serverId),
                requestAdminService.getServers(serverId),
            ]);
            setRequests(reqs);
            setServers(srvs);

            const defaults = srvs.filter(s => s.isEnabled && s.isDefault);
            const newProfileMap: Record<string, ProviderOptionDto[]> = {};

            for (const s of defaults) {
                if (s.hostname && s.apiKey && !newProfileMap[s.providerId]) {
                    try {
                        const opts = await requestAdminService.getProviderOptions({
                            providerId: s.providerId, optionType: 'qualityProfiles', hostname: s.hostname,
                            port: s.port, useSsl: s.useSsl, apiKey: s.apiKey, urlBase: s.urlBase,
                        }, serverId);
                        newProfileMap[s.providerId] = opts;
                    } catch (err) {
                        console.warn(`Failed to fetch profiles for ${s.name}:`, err);
                    }
                }
            }
            setProfilesMap(newProfileMap);

            const initialSelected: Record<string, number> = {};
            reqs.filter(r => r.status === 0).forEach(req => {
                const srv = defaults.find(s => s.mediaType === req.type);
                if (srv) {
                    try {
                        const settings = JSON.parse(srv.providerSettingsJson || '{}');
                        const validProfiles = newProfileMap[srv.providerId] || [];

                        let targetProfileId = settings.qualityProfileId;
                        if (!validProfiles.some(p => parseInt(p.id) === targetProfileId)) {
                            targetProfileId = validProfiles.length > 0 ? parseInt(validProfiles[0].id) : undefined;
                        }

                        if (targetProfileId) initialSelected[req.id] = targetProfileId;
                    } catch (err) {
                        console.warn(`Failed to parse default settings for request ${req.id}:`, err);
                    }
                }
            });
            setSelectedProfiles(initialSelected);
        } catch {
            console.error('Failed to load queue data.');
        } finally {
            setIsLoading(false);
        }
    }, [serverId]);

    useEffect(() => {
        loadData();
    }, [loadData]);

    const handleApprove = async (requestId: string) => {
        try {
            const profileId = selectedProfiles[requestId];
            await requestAdminService.approveRequest(requestId, profileId, serverId);
            await loadData();
        } catch {
            await dialog.alert('Failed to send request to the provider. Check your server settings!');
        }
    };

    const handleDelete = async (requestId: string) => {
        if (!await dialog.confirm('Are you sure you want to delete this request?')) return;
        try {
            await requestAdminService.deleteRequest(requestId, serverId);
            await loadData();
        } catch {
            await dialog.alert('Failed to delete request.');
        }
    };

    const filteredRequests = requests.filter(r => r.status === activeStatus);

    const groupedRequests = filteredRequests.reduce((acc, req) => {
        if (!acc[req.type]) acc[req.type] = [];
        acc[req.type].push(req);
        return acc;
    }, {} as Record<string, MediaRequestVM[]>);

    const statusPills: { value: number, label: string, tone: 'accent' | 'info' | 'success' }[] = [
        { value: 0, label: 'Pending', tone: 'accent' },
        { value: 3, label: 'Processing', tone: 'info' },
        { value: 4, label: 'Available', tone: 'success' },
    ];

    return (
        <div data-vora-page="">
            <PageHeader
                title="Request Queue"
                description="Manage user media requests and monitor Radarr/Sonarr download status."
            />

            <div className="px-8 pb-10 max-w-7xl mx-auto pt-2">
                <div className="flex gap-2 mb-6">
                            {statusPills.map(pill => {
                                const count = requests.filter(r => r.status === pill.value).length;
                                const isActive = activeStatus === pill.value;
                                const activeBg =
                                    pill.tone === 'accent' ? 'bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)]' :
                                    pill.tone === 'info' ? 'bg-[var(--vora-info-500)] text-[var(--vora-text-primary)]' :
                                    'bg-[var(--vora-success-500)] text-[var(--vora-text-primary)]';
                                return (
                                    <button
                                        key={pill.value}
                                        type="button"
                                        onClick={() => setActiveStatus(pill.value)}
                                        className={`px-5 py-1.5 rounded-full text-sm font-semibold transition-colors cursor-pointer ${isActive ? activeBg : 'bg-[var(--vora-bg-sunken)] text-[var(--vora-text-secondary)] hover:bg-[var(--vora-border-strong)] hover:text-[var(--vora-text-primary)]'}`}
                                    >
                                        {pill.label} <span className="opacity-70 tabular-nums">({count})</span>
                                    </button>
                                );
                            })}
                        </div>

                        {isLoading ? (
                            <div className="vora-skeleton h-48" />
                        ) : filteredRequests.length === 0 ? (
                            <div className="vora-card">
                                <EmptyState
                                    title="No requests in this category"
                                    description="When users request new media it'll appear here for you to approve or monitor."
                                />
                            </div>
                        ) : (
                            Object.entries(groupedRequests).map(([type, typeRequests]) => (
                                <div key={type} className="mb-8">
                                    <h2 className="text-xs font-bold text-[var(--vora-text-muted)] mb-3 uppercase tracking-widest">
                                        {type} Requests
                                        <span className="ml-2 text-[var(--vora-text-disabled)] font-medium normal-case tracking-normal">· {typeRequests.length}</span>
                                    </h2>
                                    <div className="grid grid-cols-1 lg:grid-cols-2 xl:grid-cols-3 gap-4">
                                        {typeRequests.map(req => {
                                            const defaultServer = servers.find(s => s.mediaType === req.type && s.isDefault && s.isEnabled);
                                            const availableProfiles = defaultServer ? profilesMap[defaultServer.providerId] : [];

                                            return (
                                                <div key={req.id} className="vora-card overflow-hidden flex">
                                                    <div
                                                        className="w-28 bg-[var(--vora-bg-sunken)] shrink-0 cursor-pointer"
                                                        onClick={() => navigate(serverId ? `/server/${serverId}/discovery/${req.providerId}/${req.type}/${req.externalId}` : `/discovery/${req.providerId}/${req.type}/${req.externalId}`)}
                                                    >
                                                        {req.posterUrl
                                                            ? <img src={req.posterUrl} alt={req.title} className="w-full h-full object-cover hover:opacity-80 transition-opacity" />
                                                            : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)] text-xs">No art</div>}
                                                    </div>
                                                    <div className="p-4 flex-1 flex flex-col min-w-0">
                                                        <div className="flex justify-between items-start mb-1 gap-2">
                                                            <h3
                                                                className="text-sm font-semibold text-[var(--vora-text-primary)] leading-tight line-clamp-2 cursor-pointer hover:text-[var(--vora-accent-text)] transition-colors"
                                                                onClick={() => navigate(serverId ? `/server/${serverId}/discovery/${req.providerId}/${req.type}/${req.externalId}` : `/discovery/${req.providerId}/${req.type}/${req.externalId}`)}
                                                            >
                                                                {req.title}
                                                            </h3>
                                                            <button
                                                                type="button"
                                                                onClick={() => handleDelete(req.id)}
                                                                className="text-[var(--vora-text-muted)] hover:text-[var(--vora-danger-500)] transition-colors cursor-pointer shrink-0 mt-0.5"
                                                                title="Delete request"
                                                            >
                                                                <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" /></svg>
                                                            </button>
                                                        </div>

                                                        <p className="text-[10px] text-[var(--vora-text-muted)] mb-2">
                                                            {new Date(req.createdAt).toLocaleDateString()} · {new Date(req.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                                                        </p>

                                                        <div className="flex flex-wrap gap-1.5 mb-3">
                                                            {req.requesters.map(ru => (
                                                                <div key={ru.profileId} className="flex items-center gap-1.5 bg-[var(--vora-bg-sunken)] px-2 py-0.5 rounded-full">
                                                                    <div className="w-4 h-4 rounded-full bg-[var(--vora-accent-500)] flex items-center justify-center text-[9px] font-bold text-[var(--vora-text-primary)] uppercase">{ru.profile.name.charAt(0)}</div>
                                                                    <span className="text-[11px] text-[var(--vora-text-secondary)] pr-1">{ru.profile.name}</span>
                                                                </div>
                                                            ))}
                                                        </div>

                                                        <div className="mt-auto">
                                                            {activeStatus === 0 && (
                                                                <>
                                                                    {defaultServer && availableProfiles && availableProfiles.length > 0 && (
                                                                        <div className="mb-3">
                                                                            <label className="block text-[10px] font-bold text-[var(--vora-text-muted)] uppercase tracking-widest mb-1">Quality Profile Override</label>
                                                                            <select
                                                                                value={selectedProfiles[req.id] || ''}
                                                                                onChange={e => setSelectedProfiles(prev => ({ ...prev, [req.id]: parseInt(e.target.value) }))}
                                                                                className="vora-input !py-1 text-xs cursor-pointer"
                                                                            >
                                                                                {availableProfiles.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                                                                            </select>
                                                                        </div>
                                                                    )}
                                                                    <button
                                                                        type="button"
                                                                        onClick={() => handleApprove(req.id)}
                                                                        className="vora-button-primary w-full text-xs"
                                                                    >
                                                                        Approve & Send
                                                                    </button>
                                                                </>
                                                            )}
                                                            {activeStatus === 3 && (
                                                                <div className="w-full flex justify-center"><HealthBadge tone="info">Sent to provider</HealthBadge></div>
                                                            )}
                                                            {activeStatus === 4 && (
                                                                <div className="w-full flex justify-center"><HealthBadge tone="ok">In library</HealthBadge></div>
                                                            )}
                                                        </div>
                                                    </div>
                                                </div>
                                            );
                                        })}
                                    </div>
                                </div>
                            ))
                        )}
            </div>
        </div>
    );
}
