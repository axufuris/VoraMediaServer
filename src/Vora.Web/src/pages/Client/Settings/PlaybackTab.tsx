import { useState, useEffect, useRef, type ChangeEvent } from 'react';
import { profileDeviceSettingsService } from '../../../api/Users/profileDeviceSettingsService';
import { useDialog } from '../../../dialogs';
import { StorageKeys } from '../../../utils/storageKeys';
import { detectAudioOutputChannels } from '../../../utils/hardwareScanner';
import { DEFAULT_PLAYER_KEY_STEPS, SEEK_STEP_CHOICES, VOLUME_STEP_CHOICES, readPlayerKeySteps } from '../../../utils/playerKeys';
import { BITRATE_CHOICES, RESOLUTION_CHOICES } from '../../../utils/playbackChoices';
import AudioSubtitleSection from './AudioSubtitleSection';
import { announcePlaybackPrefsChanged, onPlaybackPrefsChanged } from '../../../utils/playbackPrefsEvents';
import { serializePlaybackPrefs, storePlaybackPrefs } from '../../../utils/playbackPrefs';
import { errorDetail } from '../../../utils/apiError';
import DesktopPlayerSection from './DesktopPlayerSection';

export default function PlaybackTab({ activeProfileId, serverId, onSaved }: { activeProfileId: string, serverId?: string, onSaved: () => void }) {
    const dialog = useDialog();
    const [clientBitrateLimit, setClientBitrateLimit] = useState(0);
    const [maxResolution, setMaxResolution] = useState(0);
    const [maxAudioChannels, setMaxAudioChannels] = useState(0);
    const [seekStepSeconds, setSeekStepSeconds] = useState(DEFAULT_PLAYER_KEY_STEPS.seekSeconds);
    const [volumeStepPercent, setVolumeStepPercent] = useState(DEFAULT_PLAYER_KEY_STEPS.volumePercent);
    const [detectedAudioChannels, setDetectedAudioChannels] = useState(detectAudioOutputChannels);
    const [saving, setSaving] = useState(false);
    const edited = useRef(false);

    useEffect(() => {
        const devices = navigator.mediaDevices;
        const refreshOutput = () => setDetectedAudioChannels(detectAudioOutputChannels());
        devices?.addEventListener('devicechange', refreshOutput);
        return () => devices?.removeEventListener('devicechange', refreshOutput);
    }, []);

    useEffect(() => {
        const showStored = () => {
            const deviceId = localStorage.getItem(StorageKeys.deviceId) || 'unknown';
            const savedPref = localStorage.getItem(StorageKeys.playbackPrefs(activeProfileId, deviceId));
            if (!savedPref) return;
            const keySteps = readPlayerKeySteps(activeProfileId, deviceId);
            queueMicrotask(() => {
                setSeekStepSeconds(keySteps.seekSeconds);
                setVolumeStepPercent(keySteps.volumePercent);
                try {
                    const parsed = JSON.parse(savedPref);
                    if (parsed.bitrate) setClientBitrateLimit(parsed.bitrate);
                    if (parsed.maxResolution !== undefined) setMaxResolution(parsed.maxResolution);
                    if (parsed.maxAudioChannels !== undefined) setMaxAudioChannels(parsed.maxAudioChannels);
                } catch {
                    setClientBitrateLimit(parseInt(savedPref, 10) || 0);
                }
            });
        };

        showStored();
        return onPlaybackPrefsChanged(() => { if (!edited.current) showStored(); });
    }, [activeProfileId]);

    const pick = (set: (value: number) => void) => (event: ChangeEvent<HTMLSelectElement>) => {
        edited.current = true;
        set(parseInt(event.target.value, 10));
    };

    const save = async () => {
        const deviceId = localStorage.getItem(StorageKeys.deviceId) || 'unknown';
        const prefs = { bitrate: clientBitrateLimit, maxResolution, maxAudioChannels, seekStepSeconds, volumeStepPercent };

        setSaving(true);
        try {
            if (activeProfileId) {
                await profileDeviceSettingsService.saveClientSettings(activeProfileId, deviceId, { playbackPrefs: serializePlaybackPrefs(prefs) }, serverId);
            }
        } catch (error: unknown) {
            await dialog.alert(errorDetail(error, 'Could not save your playback settings'));
            return;
        } finally {
            setSaving(false);
        }

        storePlaybackPrefs(activeProfileId, deviceId, prefs);
        edited.current = false;
        announcePlaybackPrefsChanged();
        onSaved();
    };

    return (
        <div className="space-y-6">
            <AudioSubtitleSection serverId={serverId} />

            <DesktopPlayerSection />

            <section className="vora-card p-6">
                <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Bandwidth &amp; quality</h2>
                <p className="m-0 mb-5 text-sm" style={{ color: 'var(--vora-text-muted)' }}>Cap how much your device pulls per stream. Lower values help on mobile or slow connections.</p>
                <div className="grid grid-cols-1 gap-5 md:grid-cols-3">
                    <div>
                        <label className="mb-1.5 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Maximum bitrate</label>
                        <select value={clientBitrateLimit} onChange={pick(setClientBitrateLimit)} className="w-full cursor-pointer rounded-md p-2.5 text-sm outline-none" style={{ background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' }}>
                            {BITRATE_CHOICES.map(choice => <option key={choice.value} value={choice.value}>{choice.label}</option>)}
                        </select>
                    </div>
                    <div>
                        <label className="mb-1.5 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Maximum resolution</label>
                        <select value={maxResolution} onChange={pick(setMaxResolution)} className="w-full cursor-pointer rounded-md p-2.5 text-sm outline-none" style={{ background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' }}>
                            {RESOLUTION_CHOICES.map(choice => <option key={choice.value} value={choice.value}>{choice.label}</option>)}
                        </select>
                        <p className="mb-0 mt-2 text-xs" style={{ color: 'var(--vora-text-muted)' }}>Original keeps compatible video at its source resolution. Choose a limit to reduce resolution; codec, HDR and bandwidth requirements still apply.</p>
                    </div>
                    <div>
                        <label htmlFor="playback-audio-output" className="mb-1.5 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Audio output</label>
                        <select id="playback-audio-output" value={maxAudioChannels} onChange={pick(setMaxAudioChannels)} className="w-full cursor-pointer rounded-md p-2.5 text-sm outline-none" style={{ background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' }}>
                            <option value={0}>Automatic ({detectedAudioChannels} channels detected)</option>
                            <option value={2}>Stereo (2.0)</option>
                            <option value={6}>5.1 surround</option>
                            <option value={8}>7.1 surround</option>
                        </select>
                        <p className="mb-0 mt-2 text-xs" style={{ color: 'var(--vora-text-muted)' }}>Automatic uses the output your browser reports. Pick a layout to prefer audio tracks in that layout. Whether the server converts audio to match your pick depends on its streaming profile.</p>
                    </div>
                </div>
            </section>

            <section className="vora-card p-6">
                <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Keyboard</h2>
                <p className="m-0 mb-5 text-sm" style={{ color: 'var(--vora-text-muted)' }}>Space plays and pauses, and so does a click on the video. While a video plays, Left and Right skip back and forward, and Up and Down change the volume. In the music, podcast and radio players, hold Shift with the arrow keys, because the arrows on their own move between the buttons there.</p>
                <div className="grid grid-cols-1 gap-5 md:grid-cols-3">
                    <div>
                        <label htmlFor="playback-seek-step" className="mb-1.5 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Left and Right skip</label>
                        <select id="playback-seek-step" value={seekStepSeconds} onChange={pick(setSeekStepSeconds)} className="w-full cursor-pointer rounded-md p-2.5 text-sm outline-none" style={{ background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' }}>
                            {SEEK_STEP_CHOICES.map(seconds => <option key={seconds} value={seconds}>{seconds} seconds</option>)}
                        </select>
                    </div>
                    <div>
                        <label htmlFor="playback-volume-step" className="mb-1.5 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Up and Down change volume by</label>
                        <select id="playback-volume-step" value={volumeStepPercent} onChange={pick(setVolumeStepPercent)} className="w-full cursor-pointer rounded-md p-2.5 text-sm outline-none" style={{ background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' }}>
                            {VOLUME_STEP_CHOICES.map(percent => <option key={percent} value={percent}>{percent}%</option>)}
                        </select>
                    </div>
                </div>
            </section>

            <div className="flex justify-end">
                <button type="button" onClick={save} disabled={saving} className="vora-button-primary cursor-pointer disabled:cursor-not-allowed disabled:opacity-50">Save playback settings</button>
            </div>
        </div>
    );
}
