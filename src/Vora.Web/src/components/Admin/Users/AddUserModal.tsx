import { useState } from 'react';
import { Modal, ModalHeader } from '../../Common/Modal';
import { userService } from '../../../api/Users/userService';
import { resolveReason } from '../../../utils/apiError';

// An admin adding someone directly, whatever the registration mode. The admin
// sets a starting password and passes it on; the person can change it from
// their account settings once they're in.
interface AddUserModalProps {
    serverId?: string;
    onClose: () => void;
    onCreated: () => void;
}

const MIN_PASSWORD_LENGTH = 8;

// Readable when read aloud or typed from a note: no 0/O or 1/l/I.
const PASSWORD_ALPHABET = 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789';

function generatePassword(length = 12): string {
    const bytes = new Uint32Array(length);
    crypto.getRandomValues(bytes);
    return Array.from(bytes, b => PASSWORD_ALPHABET[b % PASSWORD_ALPHABET.length]).join('');
}

export default function AddUserModal({ serverId, onClose, onCreated }: AddUserModalProps) {
    const [displayName, setDisplayName] = useState('');
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState(() => generatePassword());
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [created, setCreated] = useState<{ email: string; password: string } | null>(null);

    const canSave = displayName.trim() && email.includes('@') && password.length >= MIN_PASSWORD_LENGTH && !saving;

    const handleSubmit = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        if (!canSave) return;
        setSaving(true);
        setError(null);
        try {
            await userService.createUser(email.trim(), displayName.trim(), password, serverId);
            setCreated({ email: email.trim().toLowerCase(), password });
            onCreated();
        } catch (err) {
            setError(resolveReason(err) ?? 'Could not create the account.');
        } finally {
            setSaving(false);
        }
    };

    return (
        <Modal isOpen onClose={onClose} size="sm" zIndex="z-[200]" cardClassName="p-6">
            <ModalHeader title={created ? 'Account created' : 'Add user'} onClose={onClose} bordered={false} />

            {created ? (
                <div className="space-y-4">
                    <p className="text-sm text-[var(--vora-text-secondary)]">
                        Give these to them. They can change the password from their account settings after signing in.
                    </p>
                    <dl className="space-y-2 rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] p-4 text-sm">
                        <div className="flex justify-between gap-4"><dt className="text-[var(--vora-text-muted)]">Email</dt><dd className="font-mono text-[var(--vora-text-primary)]">{created.email}</dd></div>
                        <div className="flex justify-between gap-4"><dt className="text-[var(--vora-text-muted)]">Password</dt><dd className="font-mono text-[var(--vora-text-primary)]">{created.password}</dd></div>
                    </dl>
                    <div className="flex justify-end">
                        <button type="button" onClick={onClose} className="vora-button-primary">Done</button>
                    </div>
                </div>
            ) : (
                <form onSubmit={handleSubmit} className="space-y-4">
                    <div>
                        <label htmlFor="add-user-name" className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">Name</label>
                        <input id="add-user-name" autoFocus value={displayName} onChange={e => setDisplayName(e.target.value)} className="vora-input w-full" />
                    </div>
                    <div>
                        <label htmlFor="add-user-email" className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">Email</label>
                        <input id="add-user-email" type="email" value={email} onChange={e => setEmail(e.target.value)} className="vora-input w-full" />
                    </div>
                    <div>
                        <label htmlFor="add-user-password" className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">Starting password</label>
                        <div className="flex gap-2">
                            <input id="add-user-password" value={password} onChange={e => setPassword(e.target.value)} className="vora-input w-full font-mono" />
                            <button type="button" onClick={() => setPassword(generatePassword())} className="vora-button-secondary shrink-0">New</button>
                        </div>
                        <p className="mt-1 text-xs text-[var(--vora-text-muted)]">At least {MIN_PASSWORD_LENGTH} characters. You'll see it again on the next screen.</p>
                    </div>
                    {error && <p role="alert" className="text-sm text-[var(--vora-danger-text)]">{error}</p>}
                    <div className="flex justify-end gap-2 pt-2">
                        <button type="button" onClick={onClose} className="vora-button-secondary">Cancel</button>
                        <button type="submit" disabled={!canSave} className="vora-button-primary disabled:opacity-50">
                            {saving ? 'Creating…' : 'Create account'}
                        </button>
                    </div>
                </form>
            )}
        </Modal>
    );
}
