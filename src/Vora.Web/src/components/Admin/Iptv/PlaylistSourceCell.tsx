import type { IptvPlaylistVM } from '../../../api/Iptv/iptvAdminService';
import HealthBadge from '../Primitives/HealthBadge';
import { describeSource, formatAccountDate } from './iptvSourceDraft';

export function AccountBadges({ playlist }: { playlist: IptvPlaylistVM }) {
    if (playlist.sourceType === 'HdHomeRun') {
        const tuners = playlist.accountMaxConnections;
        if (!playlist.accountStatus && !tuners) return null;
        return (
            <div className="mt-1.5 flex flex-wrap items-center gap-1.5">
                {playlist.accountStatus && <HealthBadge tone="neutral" showDot={false}>{playlist.accountStatus}</HealthBadge>}
                {tuners ? <HealthBadge tone="neutral" showDot={false}>{tuners} {tuners === 1 ? 'tuner' : 'tuners'}</HealthBadge> : null}
            </div>
        );
    }

    if (playlist.sourceType !== 'Xtream' || !playlist.accountStatus) return null;

    const active = playlist.accountStatus.toLowerCase() === 'active';
    const expiry = formatAccountDate(playlist.accountExpiresAt);
    const connections = playlist.accountMaxConnections;

    return (
        <div className="mt-1.5 flex flex-wrap items-center gap-1.5">
            <HealthBadge tone={active ? 'ok' : 'error'}>{playlist.accountStatus}</HealthBadge>
            {expiry && <HealthBadge tone="neutral" showDot={false}>Expires {expiry}</HealthBadge>}
            {connections ? <HealthBadge tone="neutral" showDot={false}>{connections} {connections === 1 ? 'connection' : 'connections'}</HealthBadge> : null}
        </div>
    );
}

export default function PlaylistSourceCell({ playlist }: { playlist: IptvPlaylistVM }) {
    const isXtream = playlist.sourceType !== 'M3u';
    return (
        <div className="min-w-0">
            <div className={`break-all text-sm text-[var(--vora-text-secondary)] ${isXtream ? '' : 'font-mono'}`}>{describeSource(playlist)}</div>
            {!isXtream && (playlist.passwordIsSet || playlist.username) && (
                <div className="mt-1 text-xs text-[var(--vora-text-muted)]">Signs in as {playlist.username}</div>
            )}
            <AccountBadges playlist={playlist} />
        </div>
    );
}
