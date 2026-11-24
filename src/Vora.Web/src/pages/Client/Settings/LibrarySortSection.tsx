import { useEffect, useState } from 'react';
import { profileService } from '../../../api/Users/profileService';
import { sortFormFrom, sortRequestFrom, type SortForm } from '../../../components/Common/ClientSettings/clientSettingsForm';
import LibrarySortFields from '../../../components/Common/ClientSettings/LibrarySortFields';
import { useDialog } from '../../../dialogs';
import { errorDetail } from '../../../utils/apiError';

export default function LibrarySortSection({ serverId }: { serverId?: string }) {
    const dialog = useDialog();
    const [form, setForm] = useState<SortForm | null>(null);
    const [dirty, setDirty] = useState(false);
    const [saving, setSaving] = useState(false);

    useEffect(() => {
        let cancelled = false;
        profileService.getMyLibrarySort(serverId)
            .then(sort => { if (!cancelled) setForm(sortFormFrom(sort)); })
            .catch(err => console.error('Failed to load the library sort order', err));
        return () => { cancelled = true; };
    }, [serverId]);

    if (!form) {
        return (
            <section className="vora-card p-6">
                <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Library sort order</h2>
                <div className="vora-skeleton mt-4 h-16" />
            </section>
        );
    }

    const update = (patch: Partial<SortForm>) => {
        setForm({ ...form, ...patch });
        setDirty(true);
    };

    const save = async () => {
        setSaving(true);
        try {
            const saved = await profileService.saveMyLibrarySort(sortRequestFrom(form), serverId);
            setForm(sortFormFrom(saved));
            setDirty(false);
        } catch (err) {
            await dialog.alert(errorDetail(err, 'Could not save your library sort order'));
        } finally {
            setSaving(false);
        }
    };

    return (
        <section className="vora-card p-6">
            <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Library sort order</h2>
            <p className="m-0 mb-5 text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                How your movie, TV and home video libraries are sorted when you open them, on every device. Sorting a library from its own page changes just that library, until you change this again.
            </p>
            <LibrarySortFields form={form} onChange={update} />
            <div className="mt-5 flex justify-end">
                <button type="button" onClick={save} disabled={saving || !dirty} className="vora-button-primary cursor-pointer disabled:cursor-not-allowed disabled:opacity-50">
                    {saving ? 'Saving…' : 'Save sort order'}
                </button>
            </div>
        </section>
    );
}
