import { useCallback, useEffect, useState } from 'react';
import { iptvAdminService, type IptvPlaylistVM } from '../../../../api/Iptv/iptvAdminService';
import { iptvEpgAdminService, type IptvEpgSourceVM } from '../../../../api/Iptv/iptvEpgAdminService';
import { podcastService, type CatalogPodcastVM } from '../../../../api/Podcasts/podcastService';
import type { SetupStepProps } from '../setupContext';
import { SetupGuideSourceCard, SetupPlaylistCard, SetupPodcastCatalogCard } from '../SetupLiveCards';
import { SetupCard, StepHeading, SwitchRow } from '../SetupParts';

export function LiveTvStep({ features, onFeatures, serverId }: SetupStepProps) {
    const [playlists, setPlaylists] = useState<IptvPlaylistVM[]>([]);
    const [sources, setSources] = useState<IptvEpgSourceVM[]>([]);
    const loadPlaylists = useCallback(() => { iptvAdminService.getPlaylists(serverId, 'Tv').then(setPlaylists).catch(() => setPlaylists([])); }, [serverId]);
    const loadSources = useCallback(() => { iptvEpgAdminService.getSources(serverId).then(setSources).catch(() => setSources([])); }, [serverId]);
    useEffect(() => { loadPlaylists(); loadSources(); }, [loadPlaylists, loadSources]);
    return (
        <>
            <StepHeading eyebrow="Live TV & radio" title="Live TV" lead="Watch live channels from an M3U playlist. Add a TV guide (EPG) so the apps can show what's on." />
            <SetupCard>
                <SwitchRow id="setup-livetv-on" label="Turn on Live TV" description="Live TV appears in the apps once a playlist with channels is added." checked={features.liveTvEnabled} onChange={on => onFeatures({ liveTvEnabled: on })} />
            </SetupCard>
            {features.liveTvEnabled && (
                <>
                    <SetupPlaylistCard kind="Tv" playlists={playlists} serverId={serverId} onChanged={loadPlaylists} />
                    <SetupGuideSourceCard sources={sources} serverId={serverId} onChanged={loadSources} />
                    <SetupCard>
                        <SwitchRow id="setup-dvr-on" label="Allow recording (DVR)" description="Users with permission can record programmes from the guide. Change where recordings go and how long they're kept in Live TV → DVR." checked={features.dvr} onChange={on => onFeatures({ dvr: on })} />
                    </SetupCard>
                </>
            )}
        </>
    );
}

export function RadioStep({ features, onFeatures, serverId }: SetupStepProps) {
    const [playlists, setPlaylists] = useState<IptvPlaylistVM[]>([]);
    const load = useCallback(() => { iptvAdminService.getPlaylists(serverId, 'Radio').then(setPlaylists).catch(() => setPlaylists([])); }, [serverId]);
    useEffect(() => { load(); }, [load]);
    return (
        <>
            <StepHeading eyebrow="Live TV & radio" title="Internet radio" lead="Stream radio stations from around the world. Free station lists from Radio Browser are ready to add." />
            <SetupCard>
                <SwitchRow id="setup-radio-on" label="Turn on Internet radio" description="A Radio tab appears in the apps once a station list is added." checked={features.internetRadioEnabled} onChange={on => onFeatures({ internetRadioEnabled: on })} />
            </SetupCard>
            {features.internetRadioEnabled && <SetupPlaylistCard kind="Radio" playlists={playlists} serverId={serverId} onChanged={load} />}
        </>
    );
}

export function PodcastsStep({ features, onFeatures, serverId }: SetupStepProps) {
    const [catalog, setCatalog] = useState<CatalogPodcastVM[]>([]);
    const load = useCallback(() => { podcastService.getCatalog(serverId).then(setCatalog).catch(() => setCatalog([])); }, [serverId]);
    useEffect(() => { load(); }, [load]);
    return (
        <>
            <StepHeading eyebrow="Podcasts" title="Podcasts" lead="Build a catalog of shows your users can subscribe to. Profiles that are allowed to can also add their own feeds." />
            <SetupCard>
                <SwitchRow id="setup-podcasts-on" label="Turn on Podcasts" description="Adds a Podcasts tab to the apps." checked={features.podcasts} onChange={on => onFeatures({ podcasts: on })} />
            </SetupCard>
            {features.podcasts && <SetupPodcastCatalogCard catalog={catalog} serverId={serverId} onChanged={load} />}
        </>
    );
}
