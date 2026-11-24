import { useState } from 'react';
import type { DeviceSettingsVM } from '../../../api/Users/profileService';
import { useDialog } from '../../../dialogs';
import { errorDetail } from '../../../utils/apiError';
import DeviceSettingsFields from './DeviceSettingsFields';

interface DeviceSettingsEditorProps {
    settings: DeviceSettingsVM;
    onSave: (settings: DeviceSettingsVM) => Promise<void>;
    saveLabel: string;
    onCancel?: () => void;
}

export default function DeviceSettingsEditor({ settings, onSave, saveLabel, onCancel }: DeviceSettingsEditorProps) {
    const dialog = useDialog();
    const [form, setForm] = useState<DeviceSettingsVM>(settings);
    const [dirty, setDirty] = useState(false);
    const [saving, setSaving] = useState(false);

    const change = (patch: Partial<DeviceSettingsVM>) => {
        setForm(current => ({ ...current, ...patch }));
        setDirty(true);
    };

    const save = async () => {
        setSaving(true);
        try {
            await onSave(form);
            setDirty(false);
        } catch (err: unknown) {
            await dialog.alert(errorDetail(err, 'Could not save the device settings'));
        } finally {
            setSaving(false);
        }
    };

    return (
        <div className="space-y-5">
            <DeviceSettingsFields settings={form} onChange={change} />
            <div className="flex justify-end gap-2">
                {onCancel && <button type="button" onClick={onCancel} disabled={saving} className="vora-button-secondary cursor-pointer">Cancel</button>}
                <button type="button" onClick={save} disabled={saving || !dirty} className="vora-button-primary cursor-pointer disabled:cursor-not-allowed disabled:opacity-50">
                    {saving ? 'Saving…' : saveLabel}
                </button>
            </div>
        </div>
    );
}
