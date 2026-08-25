import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { profileService } from '../../../api/Users/profileService';
import { useDialog } from '../../../dialogs';
import { StorageKeys } from '../../../utils/storageKeys';

export default function AccountTab({ serverId }: { serverId?: string }) {
    const profileName = localStorage.getItem(StorageKeys.profileName) || 'You';
    return (
        <div className="space-y-6">
            <section className="vora-card p-6">
                <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Signed in as</h2>
                <p className="m-0 text-2xl font-semibold" style={{ color: 'var(--vora-accent-text)' }}>{profileName}</p>
            </section>
            <section className="vora-card p-6">
                <h2 className="m-0 mb-3 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Manage your profile</h2>
                <div className="flex flex-col gap-2 sm:flex-row">
                    <Link to={serverId ? `/server/${serverId}/account` : '/account'} className="vora-button-secondary cursor-pointer text-center">Account &amp; security</Link>
                    <Link to={serverId ? `/server/${serverId}/history` : '/history'} className="vora-button-secondary cursor-pointer text-center">Watch history</Link>
                    <Link to="/profiles" className="vora-button-secondary cursor-pointer text-center">Switch profile</Link>
                </div>
            </section>
            <ShowtimesLocationSection serverId={serverId} />
            <PlaybackPreferencesSection serverId={serverId} />
        </div>
    );
}

function ShowtimesLocationSection({ serverId }: { serverId?: string }) {
    const dialog = useDialog();
    const [value, setValue] = useState('');
    const [originalValue, setOriginalValue] = useState('');
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);

    useEffect(() => {
        let cancelled = false;
        (async () => {
            try {
                const result = await profileService.getMyShowtimesLocation(serverId);
                if (cancelled) return;
                setValue(result.location ?? '');
                setOriginalValue(result.location ?? '');
            } catch (err) {
                console.error('Failed to load showtimes location', err);
            } finally {
                if (!cancelled) setLoading(false);
            }
        })();
        return () => { cancelled = true; };
    }, [serverId]);

    const trimmed = value.trim();
    const dirty = trimmed !== (originalValue ?? '').trim();

    const handleSave = async () => {
        setSaving(true);
        try {
            const result = await profileService.saveMyShowtimesLocation(trimmed === '' ? null : trimmed, serverId);
            setValue(result.location ?? '');
            setOriginalValue(result.location ?? '');
        } catch {
            await dialog.alert('Could not save your showtimes location. Please try again.');
        } finally {
            setSaving(false);
        }
    };

    const handleClear = async () => {
        setSaving(true);
        try {
            const result = await profileService.saveMyShowtimesLocation(null, serverId);
            setValue(result.location ?? '');
            setOriginalValue(result.location ?? '');
        } catch {
            await dialog.alert('Could not clear your showtimes location. Please try again.');
        } finally {
            setSaving(false);
        }
    };

    return (
        <section className="vora-card p-6">
            <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Movie showtimes location</h2>
            <p className="m-0 mb-4 text-sm" style={{ color: 'var(--vora-text-secondary)' }}>
                Set a ZIP code or city to find theaters near you when browsing showtimes. Leave blank to use the server default set by your admin.
            </p>
            {loading ? (
                <div className="vora-skeleton h-12" />
            ) : (
                <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
                    <input
                        type="text"
                        value={value}
                        onChange={e => setValue(e.target.value)}
                        placeholder="e.g. 90210, or Austin TX"
                        className="vora-input flex-1"
                        maxLength={120}
                    />
                    <div className="flex gap-2">
                        <button
                            type="button"
                            onClick={handleSave}
                            disabled={saving || !dirty}
                            className="vora-button-primary cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
                        >
                            {saving ? 'Saving…' : 'Save'}
                        </button>
                        {originalValue && (
                            <button
                                type="button"
                                onClick={handleClear}
                                disabled={saving}
                                className="vora-button-secondary cursor-pointer disabled:opacity-50"
                                title="Clear and use the server default"
                            >
                                Clear
                            </button>
                        )}
                    </div>
                </div>
            )}
        </section>
    );
}

function PlaybackPreferencesSection({ serverId }: { serverId?: string }) {
    const dialog = useDialog();
    const [autoSkipIntro, setAutoSkipIntro] = useState(false);
    const [autoSkipCredits, setAutoSkipCredits] = useState(false);
    const [minSceneSeconds, setMinSceneSeconds] = useState(15);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [dirty, setDirty] = useState(false);

    useEffect(() => {
        let cancelled = false;
        (async () => {
            try {
                const prefs = await profileService.getMyPlaybackPreferences(serverId);
                if (cancelled) return;
                setAutoSkipIntro(prefs.autoSkipIntro);
                setAutoSkipCredits(prefs.autoSkipCredits);
                setMinSceneSeconds(prefs.minimumCreditsSceneSeconds);
            } catch (err) {
                console.error('Failed to load playback preferences', err);
            } finally {
                if (!cancelled) setLoading(false);
            }
        })();
        return () => { cancelled = true; };
    }, [serverId]);

    const handleSave = async () => {
        setSaving(true);
        try {
            const saved = await profileService.saveMyPlaybackPreferences({
                autoSkipIntro,
                autoSkipCredits,
                minimumCreditsSceneSeconds: minSceneSeconds
            }, serverId);
            setAutoSkipIntro(saved.autoSkipIntro);
            setAutoSkipCredits(saved.autoSkipCredits);
            setMinSceneSeconds(saved.minimumCreditsSceneSeconds);
            setDirty(false);
        } catch {
            await dialog.alert('Could not save your playback preferences. Please try again.');
        } finally {
            setSaving(false);
        }
    };

    return (
        <section className="vora-card p-6">
            <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Playback preferences</h2>
            <p className="m-0 mb-4 text-sm" style={{ color: 'var(--vora-text-secondary)' }}>
                Choose what gets skipped automatically during playback. Credit scenes (mid-credits and post-credits stingers) are never auto-skipped — you'll always get a button to jump to the next one.
            </p>
            {loading ? (
                <div className="vora-skeleton h-24" />
            ) : (
                <div className="flex flex-col gap-4">
                    <label className="flex items-center gap-3 cursor-pointer">
                        <input
                            type="checkbox"
                            checked={autoSkipIntro}
                            onChange={e => { setAutoSkipIntro(e.target.checked); setDirty(true); }}
                            className="h-4 w-4 cursor-pointer"
                        />
                        <span style={{ color: 'var(--vora-text-primary)' }}>Auto-skip intros and recaps</span>
                    </label>
                    <label className="flex items-center gap-3 cursor-pointer">
                        <input
                            type="checkbox"
                            checked={autoSkipCredits}
                            onChange={e => { setAutoSkipCredits(e.target.checked); setDirty(true); }}
                            className="h-4 w-4 cursor-pointer"
                        />
                        <span style={{ color: 'var(--vora-text-primary)' }}>Auto-skip end credits (only when no credit scene follows)</span>
                    </label>
                    <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:gap-3">
                        <label className="text-sm" style={{ color: 'var(--vora-text-primary)' }}>
                            Hide "Skip to scene" button if next scene is within
                        </label>
                        <input
                            type="number"
                            min={0}
                            max={600}
                            value={minSceneSeconds}
                            onChange={e => { setMinSceneSeconds(Math.max(0, Math.min(600, parseInt(e.target.value || '0', 10)))); setDirty(true); }}
                            className="vora-input w-24"
                        />
                        <span className="text-sm" style={{ color: 'var(--vora-text-muted)' }}>seconds</span>
                    </div>
                    <div className="flex justify-end">
                        <button
                            type="button"
                            onClick={handleSave}
                            disabled={saving || !dirty}
                            className="vora-button-primary cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
                        >
                            {saving ? 'Saving…' : 'Save'}
                        </button>
                    </div>
                </div>
            )}
        </section>
    );
}
