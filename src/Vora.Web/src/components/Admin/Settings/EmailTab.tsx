import { useCallback, useEffect, useState } from 'react';
import { useDialog } from '../../../dialogs';
import {
    emailAdminService,
    type EmailDeliveryLogEntry,
    type EmailDeliveryStatus,
    type EmailSettings,
    type EmailTemplateKey,
    type EmailTemplateSummary,
} from '../../../api/System/emailAdminService';
import EditEmailTemplateModal from './EditEmailTemplateModal';

interface EmailTabProps {
    serverId?: string;
}

type TlsMode = 'startTls' | 'implicitSsl' | 'none';

function FieldLabel({ children }: { children: React.ReactNode }) {
    return <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">{children}</label>;
}

function FieldHint({ children }: { children: React.ReactNode }) {
    return <p className="text-xs text-[var(--vora-text-muted)] mt-2">{children}</p>;
}

function SettingsCard({ title, description, children, headingControl }: { title: string, description?: string, children: React.ReactNode, headingControl?: React.ReactNode }) {
    return (
        <section className="vora-card p-6">
            <div className="flex items-start gap-3 mb-4">
                {headingControl}
                <div>
                    <h3 className="text-base font-semibold text-[var(--vora-text-primary)]">{title}</h3>
                    {description && <p className="text-xs text-[var(--vora-text-muted)] mt-0.5">{description}</p>}
                </div>
            </div>
            {children}
        </section>
    );
}

function StatusPill({ status }: { status: EmailDeliveryStatus }) {
    const styles: Record<EmailDeliveryStatus, string> = {
        Sent: 'bg-[var(--vora-success-soft)] text-[var(--vora-success-text)]',
        Queued: 'bg-[var(--vora-bg-sunken)] text-[var(--vora-text-secondary)]',
        Failed: 'bg-[var(--vora-danger-soft)] text-[var(--vora-danger-text)]',
        Dropped: 'bg-[var(--vora-danger-soft)] text-[var(--vora-danger-text)]',
    };
    return <span className={`inline-flex items-center px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider rounded ${styles[status]}`}>{status}</span>;
}

function tlsModeFromSettings(settings: EmailSettings): TlsMode {
    if (settings.smtpUseImplicitSsl) return 'implicitSsl';
    if (settings.smtpUseStartTls) return 'startTls';
    return 'none';
}

function formatTimestamp(value: string | null): string {
    if (!value) return '';
    try {
        return new Date(value).toLocaleString();
    } catch {
        return value;
    }
}

export default function EmailTab({ serverId }: EmailTabProps) {
    const dialog = useDialog();

    const [settings, setSettings] = useState<EmailSettings | null>(null);
    const [tlsMode, setTlsMode] = useState<TlsMode>('startTls');
    const [newPassword, setNewPassword] = useState('');
    const [clearPassword, setClearPassword] = useState(false);
    const [editingPassword, setEditingPassword] = useState(false);
    const [isSaving, setIsSaving] = useState(false);

    const [testAddress, setTestAddress] = useState('');
    const [isSendingTest, setIsSendingTest] = useState(false);
    const [testResult, setTestResult] = useState<{ success: boolean, message: string } | null>(null);

    const [templates, setTemplates] = useState<EmailTemplateSummary[]>([]);
    const [editingKey, setEditingKey] = useState<EmailTemplateKey | null>(null);

    const [logs, setLogs] = useState<EmailDeliveryLogEntry[]>([]);
    const [isLoadingLogs, setIsLoadingLogs] = useState(false);

    const loadSettings = useCallback(async () => {
        try {
            const data = await emailAdminService.getSettings(serverId);
            setSettings(data);
            setTlsMode(tlsModeFromSettings(data));
            setNewPassword('');
            setClearPassword(false);
            setEditingPassword(false);
        } catch {
            await dialog.alert({ title: 'Error', message: 'Failed to load email settings.' });
        }
    }, [serverId, dialog]);

    const loadTemplates = useCallback(async () => {
        try {
            const data = await emailAdminService.listTemplates(serverId);
            setTemplates(data);
        } catch {
            await dialog.alert({ title: 'Error', message: 'Failed to load email templates.' });
        }
    }, [serverId, dialog]);

    const loadLogs = useCallback(async () => {
        setIsLoadingLogs(true);
        try {
            const data = await emailAdminService.getLog(50, serverId);
            setLogs(data);
        } catch {
            // intentionally silent — log is supplementary
        } finally {
            setIsLoadingLogs(false);
        }
    }, [serverId]);

    useEffect(() => {
        loadSettings();
        loadTemplates();
        loadLogs();
    }, [loadSettings, loadTemplates, loadLogs]);

    const handleSave = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!settings) return;
        setIsSaving(true);
        try {
            await emailAdminService.updateSettings({
                emailEnabled: settings.emailEnabled,
                smtpHost: settings.smtpHost,
                smtpPort: settings.smtpPort,
                smtpUseStartTls: tlsMode === 'startTls',
                smtpUseImplicitSsl: tlsMode === 'implicitSsl',
                smtpUsername: settings.smtpUsername,
                newSmtpPassword: clearPassword ? null : (newPassword.length > 0 ? newPassword : null),
                clearSmtpPassword: clearPassword,
                smtpFromAddress: settings.smtpFromAddress,
                smtpFromDisplayName: settings.smtpFromDisplayName,
                emailPublicBaseUrl: settings.emailPublicBaseUrl,
            }, serverId);
            await loadSettings();
            await dialog.alert({ title: 'Saved', message: 'Email settings updated.', tone: 'success' });
        } catch {
            await dialog.alert({ title: 'Error', message: 'Failed to save email settings.' });
        } finally {
            setIsSaving(false);
        }
    };

    const handleSendTest = async () => {
        if (!testAddress.trim()) {
            await dialog.alert({ title: 'Recipient required', message: 'Enter an email address to send the test to.' });
            return;
        }
        setIsSendingTest(true);
        setTestResult(null);
        try {
            const result = await emailAdminService.sendTest(testAddress.trim(), serverId);
            setTestResult({ success: result.success, message: result.message ?? (result.success ? 'Test email sent.' : 'Test failed.') });
            await loadLogs();
        } catch {
            setTestResult({ success: false, message: 'Failed to send test email.' });
        } finally {
            setIsSendingTest(false);
        }
    };

    const updateField = <K extends keyof EmailSettings>(key: K, value: EmailSettings[K]) => {
        if (!settings) return;
        setSettings({ ...settings, [key]: value });
    };

    if (!settings) return <div className="vora-skeleton h-32 mt-6" />;

    const passwordIsSet = settings.smtpPasswordIsSet;

    return (
        <div className="space-y-6 pt-2">
            <form onSubmit={handleSave} className="space-y-6">
                <SettingsCard
                    title="Email"
                    description="Configure how Vora sends transactional email — password resets, invites, request notifications."
                    headingControl={
                        <input
                            type="checkbox"
                            checked={settings.emailEnabled}
                            onChange={e => updateField('emailEnabled', e.target.checked)}
                            className="w-4 h-4 mt-1 accent-[var(--vora-accent-500)] cursor-pointer"
                            aria-label="Enable email"
                        />
                    }
                >
                    <p className="text-xs text-[var(--vora-text-muted)]">
                        When disabled, no outbound emails are sent and the forgot-password link is hidden on the sign-in page. The test button still works regardless.
                    </p>
                </SettingsCard>

                <SettingsCard title="SMTP connection">
                    <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                        <div className="md:col-span-2">
                            <FieldLabel>SMTP Host</FieldLabel>
                            <input
                                type="text"
                                value={settings.smtpHost ?? ''}
                                onChange={e => updateField('smtpHost', e.target.value || null)}
                                placeholder="smtp.gmail.com"
                                className="vora-input"
                            />
                        </div>
                        <div>
                            <FieldLabel>Port</FieldLabel>
                            <input
                                type="number"
                                min={1}
                                max={65535}
                                value={settings.smtpPort}
                                onChange={e => updateField('smtpPort', parseInt(e.target.value, 10) || 587)}
                                className="vora-input"
                            />
                        </div>
                    </div>

                    <div className="mt-5">
                        <FieldLabel>Connection security</FieldLabel>
                        <div className="flex flex-col sm:flex-row gap-3">
                            {([
                                { value: 'startTls' as TlsMode, label: 'STARTTLS (typically port 587)' },
                                { value: 'implicitSsl' as TlsMode, label: 'Implicit SSL (typically port 465)' },
                                { value: 'none' as TlsMode, label: 'None (not recommended)' },
                            ]).map(opt => (
                                <label key={opt.value} className="flex items-center gap-2 cursor-pointer">
                                    <input
                                        type="radio"
                                        name="tlsMode"
                                        value={opt.value}
                                        checked={tlsMode === opt.value}
                                        onChange={() => setTlsMode(opt.value)}
                                        className="accent-[var(--vora-accent-500)] cursor-pointer"
                                    />
                                    <span className="text-sm text-[var(--vora-text-primary)]">{opt.label}</span>
                                </label>
                            ))}
                        </div>
                    </div>

                    <div className="mt-5 grid grid-cols-1 md:grid-cols-2 gap-4">
                        <div>
                            <FieldLabel>Username</FieldLabel>
                            <input
                                type="text"
                                value={settings.smtpUsername ?? ''}
                                onChange={e => updateField('smtpUsername', e.target.value || null)}
                                placeholder="username@example.com"
                                className="vora-input"
                                autoComplete="off"
                            />
                        </div>
                        <div>
                            <FieldLabel>Password</FieldLabel>
                            {clearPassword ? (
                                <div className="flex items-center gap-3 h-[38px]">
                                    <span className="text-sm text-[var(--vora-danger-text)]">Will be cleared on save.</span>
                                    <button type="button" onClick={() => setClearPassword(false)} className="text-xs text-[var(--vora-accent-text)] hover:underline cursor-pointer">Undo</button>
                                </div>
                            ) : passwordIsSet && !editingPassword ? (
                                <div className="flex items-center gap-3 h-[38px]">
                                    <span className="text-sm text-[var(--vora-text-secondary)]">A password is saved.</span>
                                    <button type="button" onClick={() => setEditingPassword(true)} className="text-xs text-[var(--vora-accent-text)] hover:underline cursor-pointer">Change</button>
                                    <button type="button" onClick={() => setClearPassword(true)} className="text-xs text-[var(--vora-danger-text)] hover:underline cursor-pointer">Clear</button>
                                </div>
                            ) : (
                                <div className="flex items-center gap-2">
                                    <input
                                        type="password"
                                        value={newPassword}
                                        onChange={e => setNewPassword(e.target.value)}
                                        placeholder={passwordIsSet ? 'New password' : 'App password or SMTP password'}
                                        className="vora-input flex-1"
                                        autoComplete="new-password"
                                    />
                                    {passwordIsSet && (
                                        <button type="button" onClick={() => { setEditingPassword(false); setNewPassword(''); }} className="text-xs text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] cursor-pointer">Cancel</button>
                                    )}
                                </div>
                            )}
                            <FieldHint>Encrypted at rest using the server's data-protection keys.</FieldHint>
                        </div>
                    </div>

                    <div className="mt-5 grid grid-cols-1 md:grid-cols-2 gap-4">
                        <div>
                            <FieldLabel>From address</FieldLabel>
                            <input
                                type="email"
                                value={settings.smtpFromAddress ?? ''}
                                onChange={e => updateField('smtpFromAddress', e.target.value || null)}
                                placeholder="noreply@example.com"
                                className="vora-input"
                            />
                        </div>
                        <div>
                            <FieldLabel>From display name</FieldLabel>
                            <input
                                type="text"
                                value={settings.smtpFromDisplayName ?? ''}
                                onChange={e => updateField('smtpFromDisplayName', e.target.value || null)}
                                placeholder="Vora Server"
                                className="vora-input"
                            />
                        </div>
                    </div>

                    <div className="mt-5">
                        <FieldLabel>Public base URL</FieldLabel>
                        <input
                            type="url"
                            value={settings.emailPublicBaseUrl ?? ''}
                            onChange={e => updateField('emailPublicBaseUrl', e.target.value || null)}
                            placeholder="https://vora.example.com"
                            className="vora-input"
                        />
                        <FieldHint>Used to build absolute links in emails (e.g. password reset, invite). Leave blank to fall back to the request origin where available.</FieldHint>
                    </div>
                </SettingsCard>

                <button type="submit" disabled={isSaving} className="vora-button-primary">
                    {isSaving ? 'Saving…' : 'Save email settings'}
                </button>
            </form>

            <SettingsCard title="Send test email" description="Sends a one-off test message using the current SMTP settings, bypassing the queue. Works even when email is disabled.">
                <div className="flex flex-col sm:flex-row gap-3">
                    <input
                        type="email"
                        value={testAddress}
                        onChange={e => setTestAddress(e.target.value)}
                        placeholder="you@example.com"
                        className="vora-input flex-1"
                    />
                    <button
                        type="button"
                        onClick={handleSendTest}
                        disabled={isSendingTest}
                        className="vora-button-secondary"
                    >
                        {isSendingTest ? 'Sending…' : 'Send test'}
                    </button>
                </div>
                {testResult && (
                    <div className={`mt-3 px-3 py-2 rounded text-sm ${testResult.success ? 'bg-[var(--vora-success-soft)] text-[var(--vora-success-text)]' : 'bg-[var(--vora-danger-soft)] text-[var(--vora-danger-text)]'}`}>
                        {testResult.message}
                    </div>
                )}
            </SettingsCard>

            <SettingsCard title="Templates" description="Customize the subject and body of each email Vora sends. Leave fields blank to use the built-in defaults.">
                <ul className="divide-y divide-[var(--vora-border-subtle)]">
                    {templates.map(t => (
                        <li key={t.key} className="py-3 flex items-center gap-4">
                            <div className="flex-1 min-w-0">
                                <div className="flex items-center gap-2">
                                    <h4 className="text-sm font-semibold text-[var(--vora-text-primary)]">{t.displayName}</h4>
                                    {t.hasOverride && (
                                        <span className="inline-flex items-center px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wider rounded bg-[var(--vora-accent-soft)] text-[var(--vora-accent-text)]">Customized</span>
                                    )}
                                </div>
                                <p className="text-xs text-[var(--vora-text-muted)] mt-0.5">{t.description}</p>
                                {t.hasOverride && t.overrideUpdatedAt && (
                                    <p className="text-[11px] text-[var(--vora-text-muted)] mt-0.5">Last edited {formatTimestamp(t.overrideUpdatedAt)}</p>
                                )}
                            </div>
                            <button type="button" onClick={() => setEditingKey(t.key)} className="vora-button-secondary cursor-pointer">Edit</button>
                        </li>
                    ))}
                </ul>
            </SettingsCard>

            <SettingsCard title="Recent activity" description="Most recent delivery attempts. Older entries are pruned automatically.">
                {isLoadingLogs ? (
                    <div className="vora-skeleton h-24" />
                ) : logs.length === 0 ? (
                    <p className="text-sm text-[var(--vora-text-muted)]">No email has been sent from this server yet.</p>
                ) : (
                    <div className="overflow-x-auto">
                        <table className="w-full text-sm">
                            <thead>
                                <tr className="text-[10px] font-bold uppercase tracking-wider text-[var(--vora-text-muted)] border-b border-[var(--vora-border-subtle)]">
                                    <th className="text-left py-2 pr-3">Status</th>
                                    <th className="text-left py-2 pr-3">Template</th>
                                    <th className="text-left py-2 pr-3">To</th>
                                    <th className="text-left py-2 pr-3">Subject</th>
                                    <th className="text-left py-2 pr-3">Attempts</th>
                                    <th className="text-left py-2 pr-3">When</th>
                                </tr>
                            </thead>
                            <tbody>
                                {logs.map(l => (
                                    <tr key={l.id} className="border-b border-[var(--vora-border-subtle)] last:border-b-0">
                                        <td className="py-2 pr-3"><StatusPill status={l.status} /></td>
                                        <td className="py-2 pr-3 text-[var(--vora-text-secondary)]">{l.templateKey}</td>
                                        <td className="py-2 pr-3 text-[var(--vora-text-primary)] font-mono text-xs">{l.toAddress}</td>
                                        <td className="py-2 pr-3 text-[var(--vora-text-secondary)] truncate max-w-[280px]" title={l.errorMessage ?? undefined}>{l.subject}</td>
                                        <td className="py-2 pr-3 text-[var(--vora-text-secondary)]">{l.attemptCount}</td>
                                        <td className="py-2 pr-3 text-[var(--vora-text-muted)]">{formatTimestamp(l.sentAt ?? l.createdAt)}</td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            </SettingsCard>

            <EditEmailTemplateModal
                isOpen={editingKey !== null}
                templateKey={editingKey}
                serverId={serverId}
                onClose={() => setEditingKey(null)}
                onSaved={loadTemplates}
            />
        </div>
    );
}
