import { useState, useEffect } from 'react';
import { profileService } from '../../../api/Users/profileService';
import { playbackFormFrom, playbackRequestFrom, type PlaybackForm } from '../../../components/Common/ClientSettings/clientSettingsForm';
import PlaybackBehaviourFields from '../../../components/Common/ClientSettings/PlaybackBehaviourFields';
import { useDialog } from '../../../dialogs';
import { StorageKeys } from '../../../utils/storageKeys';
import LibrarySortSection from './LibrarySortSection';

export default function AccountTab({ serverId }: { serverId?: string }) {
    const profileName = localStorage.getItem(StorageKeys.profileName) || 'You';
    return (
        <div className="space-y-6">
            <section className="vora-card p-6">
                <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Signed in as</h2>
                <p className="m-0 text-2xl font-semibold" style={{ color: 'var(--vora-accent-text)' }}>{profileName}</p>
            </section>
            <LibrarySortSection serverId={serverId} />
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
    const [form, setForm] = useState<PlaybackForm | null>(null);
    const [saving, setSaving] = useState(false);
    const [dirty, setDirty] = useState(false);

    useEffect(() => {
        let cancelled = false;
        (async () => {
            try {
                const prefs = await profileService.getMyPlaybackPreferences(serverId);
                if (!cancelled) setForm(playbackFormFrom(prefs));
            } catch (err) {
                console.error('Failed to load playback preferences', err);
            }
        })();
        return () => { cancelled = true; };
    }, [serverId]);

    const update = (patch: Partial<PlaybackForm>) => {
        setForm(current => (current ? { ...current, ...patch } : current));
        setDirty(true);
    };

    const handleSave = async () => {
        if (!form) return;
        setSaving(true);
        try {
            const saved = await profileService.saveMyPlaybackPreferences(playbackRequestFrom(form), serverId);
            setForm(playbackFormFrom(saved));
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
                Choose what gets skipped automatically during playback, whether the next episode starts by itself, where Play on a show starts, and how a season's episodes are laid out. Credit scenes (mid-credits and post-credits stingers) are never auto-skipped — you'll always get a button to jump to the next one.
            </p>
            {!form ? (
                <div className="vora-skeleton h-24" />
            ) : (
                <div className="flex flex-col gap-4">
                    <PlaybackBehaviourFields form={form} onChange={update} />
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
