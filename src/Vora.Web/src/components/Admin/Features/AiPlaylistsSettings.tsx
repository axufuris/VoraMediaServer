import { useEffect, useState } from 'react';
import type { ServerSettings } from '../../../api/System/systemSettingsAdminService';
import { featureFlagsService } from '../../../api/System/featureFlagsService';

// AI playlists on the admin For You page, saved with the page's other mix
// settings. Off until an admin turns it on, because it sends each profile's
// listening summary to OpenAI. The status line says whether it is actually
// working, since the toggle alone isn't enough: it also needs For You and an
// OpenAI key.
interface AiPlaylistsSettingsProps {
    serverSettings: ServerSettings;
    onChange: (next: ServerSettings) => void;
    serverId?: string;
}

export default function AiPlaylistsSettings({ serverSettings, onChange, serverId }: AiPlaylistsSettingsProps) {
    const [active, setActive] = useState<boolean | null>(null);

    useEffect(() => {
        featureFlagsService.getFeatureFlags(serverId)
            .then(flags => setActive(flags.aiPlaylists))
            .catch(() => setActive(null));
    }, [serverId]);

    const enabled = serverSettings.enableAiMusicPlaylists;

    return (
        <section className="vora-card p-6" aria-labelledby="ai-playlists-heading">
            <h3 id="ai-playlists-heading" className="text-base font-semibold text-[var(--vora-text-primary)] mb-1 flex items-center gap-2">
                <input
                    type="checkbox"
                    aria-label="AI playlists"
                    checked={enabled}
                    onChange={e => onChange({ ...serverSettings, enableAiMusicPlaylists: e.target.checked })}
                    className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                />
                AI playlists
            </h3>
            <p className="text-sm text-[var(--vora-text-muted)] pl-6">
                A weekly "Made for you by AI" row, Bridges between genres, Blends between profiles, and a "Make me a playlist for…" box.
                Songs always come from your own library and each profile's parental controls. Uses the OpenAI key from the OpenAI plugin;
                each profile's listening summary is sent to OpenAI, and any profile can opt out in its own settings.
            </p>

            {enabled && active === false && (
                <p role="status" className="mt-3 pl-6 text-sm text-[var(--vora-warning-text)]">
                    Not running yet: it also needs For You switched on and an OpenAI API key in Plugins → OpenAI. Save to re-check.
                </p>
            )}

            <div className={`mt-4 pl-6 space-y-4 ${!enabled ? 'opacity-50' : ''}`}>
                <label className="flex items-center gap-2 text-sm text-[var(--vora-text-secondary)] cursor-pointer">
                    <input
                        type="checkbox"
                        checked={serverSettings.enableAiPlaylistRequests}
                        disabled={!enabled}
                        onChange={e => onChange({ ...serverSettings, enableAiPlaylistRequests: e.target.checked })}
                        className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                    />
                    Allow "Make me a playlist for…" requests
                </label>
                <div className="max-w-xs">
                    <label htmlFor="ai-requests-per-day" className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">Requests per profile per day</label>
                    <input
                        id="ai-requests-per-day"
                        type="number"
                        min={1}
                        max={100}
                        value={serverSettings.aiPlaylistRequestsPerDay}
                        disabled={!enabled || !serverSettings.enableAiPlaylistRequests}
                        onChange={e => onChange({ ...serverSettings, aiPlaylistRequestsPerDay: parseInt(e.target.value, 10) || 10 })}
                        className="vora-input"
                    />
                    <p className="text-xs text-[var(--vora-text-muted)] mt-1.5">Each request costs a fraction of a cent. The weekly playlists are one small request per profile per week.</p>
                </div>
            </div>
        </section>
    );
}
