import { useState } from 'react';
import type { ClientSettingsVM, SaveClientSettingsRequest } from '../../../api/Users/profileService';
import { useDialog } from '../../../dialogs';
import { errorDetail } from '../../../utils/apiError';
import {
    clientSettingsFormFrom,
    clientSettingsRequestFrom,
    type ClientSettingsForm,
} from './clientSettingsForm';
import LibrarySortFields from './LibrarySortFields';
import PlaybackBehaviourFields from './PlaybackBehaviourFields';
import TrackPreferenceFields from './TrackPreferenceFields';

interface ClientSettingsEditorProps {
    settings: ClientSettingsVM;
    onSave: (request: SaveClientSettingsRequest) => Promise<ClientSettingsVM>;
    onSaved?: (saved: ClientSettingsVM) => void;
    saveLabel?: string;
}

function Group({ title, description, children }: { title: string; description: string; children: React.ReactNode }) {
    return (
        <section className="border-b border-[var(--vora-border-subtle)] pb-6 last:border-b-0 last:pb-0">
            <h3 className="m-0 mb-1 text-sm font-semibold text-[var(--vora-text-primary)]">{title}</h3>
            <p className="m-0 mb-4 text-xs text-[var(--vora-text-muted)]">{description}</p>
            {children}
        </section>
    );
}

export default function ClientSettingsEditor({ settings, onSave, onSaved, saveLabel = 'Save' }: ClientSettingsEditorProps) {
    const dialog = useDialog();
    const [loaded, setLoaded] = useState(settings);
    const [form, setForm] = useState<ClientSettingsForm>(() => clientSettingsFormFrom(settings));
    const [dirty, setDirty] = useState(false);
    const [saving, setSaving] = useState(false);

    const change = <K extends keyof ClientSettingsForm>(section: K, patch: Partial<ClientSettingsForm[K]>) => {
        setForm(current => ({ ...current, [section]: { ...current[section], ...patch } }));
        setDirty(true);
    };

    const save = async () => {
        setSaving(true);
        try {
            const saved = await onSave(clientSettingsRequestFrom(form));
            setLoaded(saved);
            setForm(clientSettingsFormFrom(saved));
            setDirty(false);
            onSaved?.(saved);
        } catch (err) {
            await dialog.alert(errorDetail(err, 'Could not save the settings'));
        } finally {
            setSaving(false);
        }
    };

    return (
        <div className="space-y-6">
            <Group title="Playback" description="What is skipped and played automatically, where Play on a show starts, and how a season's episodes are laid out.">
                <PlaybackBehaviourFields form={form.playback} onChange={patch => change('playback', patch)} />
            </Group>
            <Group title="Audio and subtitles" description="The language and subtitle behaviour for every title. A track picked on a title is remembered for that title and takes priority.">
                <TrackPreferenceFields
                    form={form.tracks}
                    serverLanguage={loaded.tracks.serverLanguage}
                    serverSubtitleMode={loaded.tracks.serverSubtitleMode}
                    onChange={patch => change('tracks', patch)}
                />
            </Group>
            <Group title="Library sort order" description="How movie, TV and home video libraries are sorted when they are opened.">
                <LibrarySortFields form={form.sort} onChange={patch => change('sort', patch)} />
            </Group>
            <div className="flex justify-end">
                <button type="button" onClick={save} disabled={saving || !dirty} className="vora-button-primary cursor-pointer disabled:cursor-not-allowed disabled:opacity-50">
                    {saving ? 'Saving…' : saveLabel}
                </button>
            </div>
        </div>
    );
}
