import { useEffect, useState } from 'react';
import { Modal, ModalFooter, ModalHeader } from '../../Common/Modal';
import { useDialog } from '../../../dialogs';
import {
    emailAdminService,
    type EmailTemplateDetail,
    type EmailTemplateKey,
} from '../../../api/System/emailAdminService';

interface EditEmailTemplateModalProps {
    isOpen: boolean;
    templateKey: EmailTemplateKey | null;
    serverId?: string;
    onClose: () => void;
    onSaved: () => void;
}

export default function EditEmailTemplateModal({ isOpen, templateKey, serverId, onClose, onSaved }: EditEmailTemplateModalProps) {
    const dialog = useDialog();
    const [detail, setDetail] = useState<EmailTemplateDetail | null>(null);
    const [subject, setSubject] = useState('');
    const [htmlBody, setHtmlBody] = useState('');
    const [textBody, setTextBody] = useState('');
    const [isLoading, setIsLoading] = useState(false);
    const [isSaving, setIsSaving] = useState(false);
    const [activePane, setActivePane] = useState<'html' | 'text'>('html');

    useEffect(() => {
        if (!isOpen || !templateKey) return;
        let cancelled = false;
        setIsLoading(true);
        emailAdminService.getTemplate(templateKey, serverId)
            .then(data => {
                if (cancelled) return;
                setDetail(data);
                setSubject(data.subjectOverride ?? '');
                setHtmlBody(data.htmlBodyOverride ?? '');
                setTextBody(data.textBodyOverride ?? '');
            })
            .catch(async () => {
                if (cancelled) return;
                await dialog.alert({ title: 'Error', message: 'Failed to load the email template.' });
                onClose();
            })
            .finally(() => {
                if (!cancelled) setIsLoading(false);
            });
        return () => { cancelled = true; };
    }, [isOpen, templateKey, serverId, dialog, onClose]);

    const handleSave = async () => {
        if (!templateKey || !detail) return;
        setIsSaving(true);
        try {
            await emailAdminService.updateTemplate(templateKey, {
                subjectOverride: subject.trim() ? subject : null,
                htmlBodyOverride: htmlBody.trim() ? htmlBody : null,
                textBodyOverride: textBody.trim() ? textBody : null,
            }, serverId);
            onSaved();
            onClose();
        } catch {
            await dialog.alert({ title: 'Error', message: 'Failed to save the template override.' });
        } finally {
            setIsSaving(false);
        }
    };

    const handleRevert = async () => {
        if (!templateKey || !detail) return;
        const confirmed = await dialog.confirm({
            title: 'Revert to default?',
            message: 'This will remove your custom overrides and restore the built-in template.',
            confirmText: 'Revert',
            cancelText: 'Cancel',
            tone: 'danger',
        });
        if (!confirmed) return;
        setIsSaving(true);
        try {
            await emailAdminService.deleteTemplate(templateKey, serverId);
            onSaved();
            onClose();
        } catch {
            await dialog.alert({ title: 'Error', message: 'Failed to revert the template.' });
        } finally {
            setIsSaving(false);
        }
    };

    const fillFromDefault = () => {
        if (!detail) return;
        if (!subject) setSubject(detail.defaultSubject);
        if (!htmlBody) setHtmlBody(detail.defaultHtmlBody);
        if (!textBody) setTextBody(detail.defaultTextBody);
    };

    return (
        <Modal isOpen={isOpen} onClose={onClose} size="4xl" surface="light" cardClassName="flex flex-col max-h-[90vh]">
            <ModalHeader
                surface="light"
                title={detail ? `Edit: ${detail.displayName}` : 'Edit email template'}
                subtitle={detail?.description}
                onClose={onClose}
                closeDisabled={isSaving}
            />
            <div className="flex-1 overflow-y-auto">
                {isLoading || !detail ? (
                    <div className="p-6"><div className="vora-skeleton h-64" /></div>
                ) : (
                    <div className="grid grid-cols-1 md:grid-cols-[1fr_240px] gap-0 min-h-full">
                        <div className="p-6 space-y-5">
                            <div>
                                <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">Subject</label>
                                <input
                                    type="text"
                                    value={subject}
                                    onChange={e => setSubject(e.target.value)}
                                    placeholder={detail.defaultSubject}
                                    className="vora-input w-full"
                                />
                                <p className="text-xs text-[var(--vora-text-muted)] mt-1.5">Leave blank to use the default: <span className="font-mono">{detail.defaultSubject}</span></p>
                            </div>

                            <div>
                                <div className="flex items-center gap-4 mb-2">
                                    <button
                                        type="button"
                                        onClick={() => setActivePane('html')}
                                        className={`text-xs font-bold uppercase tracking-widest cursor-pointer pb-1 border-b-2 transition-colors ${activePane === 'html' ? 'text-[var(--vora-text-primary)] border-[var(--vora-accent-500)]' : 'text-[var(--vora-text-muted)] border-transparent'}`}
                                    >
                                        HTML Body
                                    </button>
                                    <button
                                        type="button"
                                        onClick={() => setActivePane('text')}
                                        className={`text-xs font-bold uppercase tracking-widest cursor-pointer pb-1 border-b-2 transition-colors ${activePane === 'text' ? 'text-[var(--vora-text-primary)] border-[var(--vora-accent-500)]' : 'text-[var(--vora-text-muted)] border-transparent'}`}
                                    >
                                        Plain Text Body
                                    </button>
                                    <button
                                        type="button"
                                        onClick={fillFromDefault}
                                        className="ml-auto text-xs text-[var(--vora-accent-text)] hover:underline cursor-pointer"
                                    >
                                        Copy defaults into empty fields
                                    </button>
                                </div>
                                {activePane === 'html' ? (
                                    <textarea
                                        value={htmlBody}
                                        onChange={e => setHtmlBody(e.target.value)}
                                        placeholder={detail.defaultHtmlBody}
                                        spellCheck={false}
                                        className="vora-input w-full font-mono text-xs leading-relaxed"
                                        rows={20}
                                    />
                                ) : (
                                    <textarea
                                        value={textBody}
                                        onChange={e => setTextBody(e.target.value)}
                                        placeholder={detail.defaultTextBody}
                                        spellCheck={false}
                                        className="vora-input w-full font-mono text-xs leading-relaxed"
                                        rows={20}
                                    />
                                )}
                                <p className="text-xs text-[var(--vora-text-muted)] mt-1.5">Leave blank to use the built-in default.</p>
                            </div>
                        </div>

                        <aside className="bg-[var(--vora-bg-sunken)] border-l border-[var(--vora-border-subtle)] p-5 overflow-y-auto">
                            <h3 className="text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-3">Available variables</h3>
                            <ul className="space-y-3">
                                {detail.availableVariables.map(v => (
                                    <li key={v.name}>
                                        <code className="text-xs font-mono text-[var(--vora-accent-text)]">{`{{${v.name}}}`}</code>
                                        <p className="text-xs text-[var(--vora-text-secondary)] mt-0.5">{v.description}</p>
                                    </li>
                                ))}
                            </ul>
                            <p className="text-xs text-[var(--vora-text-muted)] mt-4">HTML body values are auto-escaped. Plain text is passed through as-is.</p>
                        </aside>
                    </div>
                )}
            </div>
            <ModalFooter surface="light">
                <div className="flex items-center gap-3">
                    {detail?.hasOverride && (
                        <button
                            type="button"
                            onClick={handleRevert}
                            disabled={isSaving}
                            className="text-sm text-[var(--vora-danger-text)] hover:underline cursor-pointer disabled:opacity-50"
                        >
                            Revert to default
                        </button>
                    )}
                    <div className="ml-auto flex items-center gap-2">
                        <button
                            type="button"
                            onClick={onClose}
                            disabled={isSaving}
                            className="vora-button-secondary"
                        >
                            Cancel
                        </button>
                        <button
                            type="button"
                            onClick={handleSave}
                            disabled={isSaving || isLoading}
                            className="vora-button-primary"
                        >
                            {isSaving ? 'Saving…' : 'Save changes'}
                        </button>
                    </div>
                </div>
            </ModalFooter>
        </Modal>
    );
}
