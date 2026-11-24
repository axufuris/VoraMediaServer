import { useEffect, useState } from 'react';
import { profileService, type TrackPreferencesVM } from '../../../api/Users/profileService';
import { trackFormFrom, trackRequestFrom, type TrackForm } from '../../../components/Common/ClientSettings/clientSettingsForm';
import TrackPreferenceFields from '../../../components/Common/ClientSettings/TrackPreferenceFields';
import { useDialog } from '../../../dialogs';
import { errorDetail } from '../../../utils/apiError';

export default function AudioSubtitleSection({ serverId }: { serverId?: string }) {
    const dialog = useDialog();
    const [loaded, setLoaded] = useState<TrackPreferencesVM | null>(null);
    const [form, setForm] = useState<TrackForm | null>(null);
    const [dirty, setDirty] = useState(false);
    const [saving, setSaving] = useState(false);

    useEffect(() => {
        let cancelled = false;
        profileService.getMyTrackPreferences(serverId)
            .then(preferences => {
                if (cancelled) return;
                setLoaded(preferences);
                setForm(trackFormFrom(preferences));
            })
            .catch(err => console.error('Failed to load audio and subtitle preferences', err));
        return () => { cancelled = true; };
    }, [serverId]);

    const update = (patch: Partial<TrackForm>) => {
        setForm(current => (current ? { ...current, ...patch } : current));
        setDirty(true);
    };

    if (!loaded || !form) {
        return (
            <section className="vora-card p-6">
                <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Audio &amp; subtitles</h2>
                <div className="vora-skeleton mt-4 h-24" />
            </section>
        );
    }

    const save = async () => {
        setSaving(true);
        try {
            const saved = await profileService.saveMyTrackPreferences(trackRequestFrom(form), serverId);
            setLoaded(saved);
            setForm(trackFormFrom(saved));
            setDirty(false);
        } catch (err) {
            await dialog.alert(errorDetail(err, 'Could not save your audio and subtitle preferences'));
        } finally {
            setSaving(false);
        }
    };

    return (
        <section className="vora-card p-6">
            <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Audio &amp; subtitles</h2>
            <p className="m-0 mb-5 text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                Saved to your profile, so every device you watch on uses them. A track you pick on a title's page or in the player is remembered for that title and takes priority.
            </p>
            <TrackPreferenceFields form={form} serverLanguage={loaded.serverLanguage} serverSubtitleMode={loaded.serverSubtitleMode} onChange={update} />
            <div className="mt-5 flex justify-end">
                <button type="button" onClick={save} disabled={saving || !dirty} className="vora-button-primary cursor-pointer disabled:cursor-not-allowed disabled:opacity-50">
                    {saving ? 'Saving…' : 'Save audio & subtitles'}
                </button>
            </div>
        </section>
    );
}
