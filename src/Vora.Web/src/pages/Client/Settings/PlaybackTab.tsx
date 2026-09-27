import { useState, useEffect } from 'react';
import { profileDeviceSettingsService } from '../../../api/Users/profileDeviceSettingsService';
import { StorageKeys } from '../../../utils/storageKeys';

export default function PlaybackTab({ activeProfileId, serverId, onSaved }: { activeProfileId: string, serverId?: string, onSaved: () => void }) {
    const [clientBitrateLimit, setClientBitrateLimit] = useState(0);
    const [maxResolution, setMaxResolution] = useState(0);
    const [maxAudioChannels, setMaxAudioChannels] = useState(0);

    useEffect(() => {
        const deviceId = localStorage.getItem(StorageKeys.deviceId) || 'unknown';
        const savedPref = localStorage.getItem(`playback_prefs_${activeProfileId}_${deviceId}`);
        if (!savedPref) return;
        queueMicrotask(() => {
            try {
                const parsed = JSON.parse(savedPref);
                if (parsed.bitrate) setClientBitrateLimit(parsed.bitrate);
                if (parsed.maxResolution !== undefined) setMaxResolution(parsed.maxResolution);
                if (parsed.maxAudioChannels !== undefined) setMaxAudioChannels(parsed.maxAudioChannels);
            } catch {
                setClientBitrateLimit(parseInt(savedPref, 10) || 0);
            }
        });
    }, [activeProfileId]);

    const save = async () => {
        const deviceId = localStorage.getItem(StorageKeys.deviceId) || 'unknown';
        const prefsKey = `playback_prefs_${activeProfileId}_${deviceId}`;
        const prefsString = JSON.stringify({ bitrate: clientBitrateLimit, maxResolution, maxAudioChannels });
        localStorage.setItem(prefsKey, prefsString);

        if (activeProfileId) {
            try {
                const iptvKey = `iptv_prefs_${activeProfileId}_${deviceId}`;
                const iptvPrefsString = localStorage.getItem(iptvKey) || '{}';
                await profileDeviceSettingsService.saveClientSettings(activeProfileId, deviceId, prefsString, iptvPrefsString, serverId);
            } catch (error) {
                console.error('Failed to save client settings to server:', error);
            }
        }
        onSaved();
    };

    return (
        <div className="space-y-6">
            <section className="vora-card p-6">
                <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Bandwidth &amp; quality</h2>
                <p className="m-0 mb-5 text-sm" style={{ color: 'var(--vora-text-muted)' }}>Cap how much your device pulls per stream. Lower values help on mobile or slow connections.</p>
                <div className="grid grid-cols-1 gap-5 md:grid-cols-3">
                    <div>
                        <label className="mb-1.5 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Maximum bitrate</label>
                        <select value={clientBitrateLimit} onChange={e => setClientBitrateLimit(parseInt(e.target.value, 10))} className="w-full cursor-pointer rounded-md p-2.5 text-sm outline-none" style={{ background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' }}>
                            <option value={0}>Original (no limit)</option>
                            <option value={40}>40 Mbps</option>
                            <option value={20}>20 Mbps</option>
                            <option value={10}>10 Mbps</option>
                            <option value={4}>4 Mbps</option>
                        </select>
                    </div>
                    <div>
                        <label className="mb-1.5 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Maximum resolution</label>
                        <select value={maxResolution} onChange={e => setMaxResolution(parseInt(e.target.value, 10))} className="w-full cursor-pointer rounded-md p-2.5 text-sm outline-none" style={{ background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' }}>
                            <option value={0}>Original (device limit)</option>
                            <option value={2160}>4K (2160p)</option>
                            <option value={1080}>HD (1080p)</option>
                            <option value={720}>HD (720p)</option>
                        </select>
                    </div>
                    <div>
                        <label className="mb-1.5 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Max audio channels</label>
                        <select value={maxAudioChannels} onChange={e => setMaxAudioChannels(parseInt(e.target.value, 10))} className="w-full cursor-pointer rounded-md p-2.5 text-sm outline-none" style={{ background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' }}>
                            <option value={0}>Device capability</option>
                            <option value={2}>Stereo (2.0)</option>
                            <option value={6}>5.1 surround</option>
                            <option value={8}>7.1 surround</option>
                        </select>
                    </div>
                </div>
            </section>

            <div className="flex justify-end">
                <button type="button" onClick={save} className="vora-button-primary cursor-pointer">Save playback settings</button>
            </div>
        </div>
    );
}
