import { useState } from 'react';
import { Modal, ModalHeader } from '../../Common/Modal';
import StarRating from './StarRating';

interface RateDialogProps {
    title: string;
    value: number | null;
    onSave: (next: number | null) => void | Promise<void>;
    onClose: () => void;
}

export default function RateDialog({ title, value, onSave, onClose }: RateDialogProps) {
    const [draft, setDraft] = useState<number | null>(value);
    const [saving, setSaving] = useState(false);

    const save = async (next: number | null) => {
        setSaving(true);
        try {
            await onSave(next);
            onClose();
        } finally {
            setSaving(false);
        }
    };

    return (
        <Modal isOpen onClose={onClose} size="sm" zIndex="z-[200]" cardClassName="p-6">
            <ModalHeader title={`Rate ${title}`} onClose={onClose} bordered={false} />
            <div className="flex flex-col items-center gap-3 py-2">
                <StarRating value={draft} onChange={setDraft} size={32} showNumeric ariaLabel="Your rating" />
                <p className="text-xs text-[var(--vora-text-muted)]">Click a star, or its left half for a half star.</p>
            </div>
            <div className="mt-4 flex justify-end gap-2">
                {value != null && (
                    <button type="button" onClick={() => save(null)} disabled={saving} className="vora-button-secondary mr-auto disabled:opacity-50">
                        Clear rating
                    </button>
                )}
                <button type="button" onClick={onClose} className="vora-button-secondary">Cancel</button>
                <button type="button" onClick={() => save(draft)} disabled={saving || draft === value} className="vora-button-primary disabled:opacity-50">
                    Save
                </button>
            </div>
        </Modal>
    );
}
