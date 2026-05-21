import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { invitationsAdminService, type Invitation } from '../../api/Auth/invitationsAdminService';
import { authService } from '../../api/Auth/authService';
import { useDialog } from '../../dialogs';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import { Modal, ModalBody, ModalFooter, ModalHeader } from '../../components/Common/Modal';

function formatTimestamp(value: string): string {
    try {
        return new Date(value).toLocaleString();
    } catch {
        return value;
    }
}

function formatRelativeExpiry(value: string): string {
    const expiresAt = new Date(value).getTime();
    const diffMs = expiresAt - Date.now();
    if (diffMs <= 0) return 'expired';
    const diffMinutes = Math.round(diffMs / 60_000);
    if (diffMinutes < 60) return `in ${diffMinutes} min`;
    const diffHours = Math.round(diffMinutes / 60);
    if (diffHours < 48) return `in ${diffHours} hr`;
    const diffDays = Math.round(diffHours / 24);
    return `in ${diffDays} days`;
}

export default function InvitationsPage() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();

    const [invitations, setInvitations] = useState<Invitation[]>([]);
    const [emailEnabled, setEmailEnabled] = useState(false);
    const [loading, setLoading] = useState(true);
    const [modalOpen, setModalOpen] = useState(false);

    const [inviteEmail, setInviteEmail] = useState('');
    const [expiresInDays, setExpiresInDays] = useState(7);
    const [isSending, setIsSending] = useState(false);
    const [feedback, setFeedback] = useState<{ kind: 'success' | 'error' | 'warning'; message: string } | null>(null);

    const loadData = useCallback(async () => {
        setLoading(true);
        try {
            const [list, setupStatus] = await Promise.all([
                invitationsAdminService.list(serverId).catch(() => [] as Invitation[]),
                authService.getSetupStatus(serverId).catch(() => null),
            ]);
            setInvitations(list);
            setEmailEnabled(setupStatus?.emailEnabled ?? false);
        } finally {
            setLoading(false);
        }
    }, [serverId]);

    useEffect(() => {
        loadData();
    }, [loadData]);

    const handleOpenInviteModal = () => {
        setInviteEmail('');
        setExpiresInDays(7);
        setFeedback(null);
        setModalOpen(true);
    };

    const handleSendInvite = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!inviteEmail.trim()) return;
        setIsSending(true);
        setFeedback(null);
        try {
            const result = await invitationsAdminService.create(inviteEmail.trim(), expiresInDays, serverId);
            if (result.emailSent) {
                setFeedback({ kind: 'success', message: `Invitation sent to ${result.invitation.email}.` });
            } else {
                setFeedback({ kind: 'warning', message: `Invitation created, but email delivery failed: ${result.message ?? 'unknown error'}. The link is still valid; you can revoke and re-send.` });
            }
            await loadData();
        } catch (err) {
            const message = (err as { response?: { status?: number; data?: { message?: string } } })?.response;
            if (message?.status === 409) {
                setFeedback({ kind: 'error', message: message?.data?.message ?? 'An account already exists for that email.' });
            } else {
                setFeedback({ kind: 'error', message: message?.data?.message ?? 'Failed to create invitation.' });
            }
        } finally {
            setIsSending(false);
        }
    };

    const handleRevoke = async (invitation: Invitation) => {
        const confirmed = await dialog.confirm({
            title: 'Revoke invitation?',
            message: `The link sent to ${invitation.email} will stop working immediately.`,
            confirmText: 'Revoke',
            cancelText: 'Cancel',
            tone: 'danger',
        });
        if (!confirmed) return;
        try {
            await invitationsAdminService.revoke(invitation.id, serverId);
            await loadData();
        } catch {
            await dialog.alert({ title: 'Error', message: 'Failed to revoke the invitation.' });
        }
    };

    return (
        <div data-vora-page="">
            <PageHeader
                title="Email Invitations"
                description="Invite specific people to register. Each invitation includes a one-time link tied to the recipient's email address."
                actions={
                    <button
                        type="button"
                        onClick={handleOpenInviteModal}
                        disabled={!emailEnabled}
                        className="vora-button-primary disabled:opacity-50 disabled:cursor-not-allowed cursor-pointer"
                    >
                        Send invitation
                    </button>
                }
            />

            <div className="px-8 pt-2 pb-10 max-w-5xl mx-auto space-y-6">
                {!emailEnabled && !loading && (
                    <div className="vora-card p-5">
                        <h3 className="text-base font-semibold text-[var(--vora-text-primary)] mb-1">Email isn't enabled</h3>
                        <p className="text-sm text-[var(--vora-text-secondary)]">
                            Invitations are delivered by email, so you'll need to enable and configure SMTP before you can send any. Open <span className="font-semibold text-[var(--vora-text-primary)]">System Settings → Email</span> to set it up.
                        </p>
                    </div>
                )}

                <section className="vora-card p-0 overflow-hidden">
                    <header className="px-6 py-4 border-b border-[var(--vora-border-subtle)]">
                        <h2 className="text-base font-semibold text-[var(--vora-text-primary)]">Outstanding invitations</h2>
                        <p className="text-xs text-[var(--vora-text-muted)] mt-0.5">Invitations are removed automatically when they're accepted or when they expire.</p>
                    </header>

                    {loading ? (
                        <div className="p-6"><div className="vora-skeleton h-24" /></div>
                    ) : invitations.length === 0 ? (
                        <div className="p-8 text-center text-sm text-[var(--vora-text-muted)]">
                            No outstanding invitations.
                        </div>
                    ) : (
                        <div className="overflow-x-auto">
                            <table className="w-full text-sm">
                                <thead>
                                    <tr className="text-[10px] font-bold uppercase tracking-wider text-[var(--vora-text-muted)] border-b border-[var(--vora-border-subtle)]">
                                        <th className="text-left py-3 pl-6 pr-3">Email</th>
                                        <th className="text-left py-3 pr-3">Created</th>
                                        <th className="text-left py-3 pr-3">Expires</th>
                                        <th className="text-right py-3 pr-6">Actions</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {invitations.map(invitation => (
                                        <tr key={invitation.id} className="border-b border-[var(--vora-border-subtle)] last:border-b-0">
                                            <td className="py-3 pl-6 pr-3 font-mono text-[var(--vora-text-primary)]">{invitation.email}</td>
                                            <td className="py-3 pr-3 text-[var(--vora-text-secondary)]">{formatTimestamp(invitation.createdAt)}</td>
                                            <td className="py-3 pr-3 text-[var(--vora-text-secondary)]">{formatRelativeExpiry(invitation.expiresAt)} <span className="text-[var(--vora-text-muted)]">({formatTimestamp(invitation.expiresAt)})</span></td>
                                            <td className="py-3 pr-6 text-right">
                                                <button
                                                    type="button"
                                                    onClick={() => handleRevoke(invitation)}
                                                    className="text-sm text-[var(--vora-danger-text)] hover:underline cursor-pointer"
                                                >
                                                    Revoke
                                                </button>
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    )}
                </section>
            </div>

            <Modal isOpen={modalOpen} onClose={() => setModalOpen(false)} size="md" surface="light">
                <form onSubmit={handleSendInvite}>
                    <ModalHeader
                        surface="light"
                        title="Send invitation"
                        subtitle="The recipient will get an email with a single-use link to register."
                        onClose={() => setModalOpen(false)}
                        closeDisabled={isSending}
                    />
                    <ModalBody>
                        <div className="space-y-5">
                            <div>
                                <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">Email</label>
                                <input
                                    autoFocus
                                    type="email"
                                    required
                                    value={inviteEmail}
                                    onChange={e => setInviteEmail(e.target.value)}
                                    placeholder="friend@example.com"
                                    className="vora-input w-full"
                                />
                            </div>
                            <div>
                                <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">Expires in (days)</label>
                                <input
                                    type="number"
                                    min={1}
                                    max={60}
                                    value={expiresInDays}
                                    onChange={e => setExpiresInDays(Math.max(1, Math.min(60, parseInt(e.target.value, 10) || 7)))}
                                    className="vora-input w-32"
                                />
                                <p className="text-xs text-[var(--vora-text-muted)] mt-1.5">Default is 7 days. Maximum 60.</p>
                            </div>

                            {feedback && (
                                <div className={`px-3 py-2 rounded text-sm ${
                                    feedback.kind === 'success' ? 'bg-[var(--vora-success-soft)] text-[var(--vora-success-text)]' :
                                    feedback.kind === 'warning' ? 'bg-[var(--vora-accent-soft)] text-[var(--vora-accent-text)]' :
                                    'bg-[var(--vora-danger-soft)] text-[var(--vora-danger-text)]'
                                }`}>
                                    {feedback.message}
                                </div>
                            )}
                        </div>
                    </ModalBody>
                    <ModalFooter surface="light">
                        <div className="flex justify-end gap-2">
                            <button
                                type="button"
                                onClick={() => setModalOpen(false)}
                                disabled={isSending}
                                className="vora-button-secondary"
                            >
                                Close
                            </button>
                            <button
                                type="submit"
                                disabled={isSending}
                                className="vora-button-primary"
                            >
                                {isSending ? 'Sending…' : 'Send invitation'}
                            </button>
                        </div>
                    </ModalFooter>
                </form>
            </Modal>
        </div>
    );
}
