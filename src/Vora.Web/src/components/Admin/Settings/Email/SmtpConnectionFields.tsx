import type { ReactNode } from 'react';
import type { EmailSettings } from '../../../../api/System/emailAdminService';
import type { SmtpPasswordDraft, TlsMode } from './smtpSettings';

interface SmtpConnectionFieldsProps {
    idPrefix: string;
    settings: EmailSettings;
    onChange: (patch: Partial<EmailSettings>) => void;
    tlsMode: TlsMode;
    onTlsMode: (mode: TlsMode) => void;
    password: SmtpPasswordDraft;
    onPassword: (patch: Partial<SmtpPasswordDraft>) => void;
    publicUrlHint?: ReactNode;
}

const SECURITY_OPTIONS: { value: TlsMode; label: string }[] = [
    { value: 'startTls', label: 'STARTTLS (usually port 587)' },
    { value: 'implicitSsl', label: 'SSL/TLS (usually port 465)' },
    { value: 'none', label: 'None (not recommended)' },
];

function FieldLabel({ htmlFor, children }: { htmlFor?: string; children: ReactNode }) {
    return <label htmlFor={htmlFor} className="mb-1.5 block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">{children}</label>;
}

function FieldHint({ children }: { children: ReactNode }) {
    return <p className="mt-2 text-xs text-[var(--vora-text-muted)]">{children}</p>;
}

export default function SmtpConnectionFields({ idPrefix, settings, onChange, tlsMode, onTlsMode, password, onPassword, publicUrlHint }: SmtpConnectionFieldsProps) {
    const id = (name: string) => `${idPrefix}-${name}`;
    const passwordIsSet = settings.smtpPasswordIsSet;

    return (
        <div className="flex flex-col gap-5">
            <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
                <div className="md:col-span-2">
                    <FieldLabel htmlFor={id('host')}>SMTP server</FieldLabel>
                    <input id={id('host')} type="text" value={settings.smtpHost ?? ''} onChange={e => onChange({ smtpHost: e.target.value || null })} placeholder="smtp.gmail.com" className="vora-input" autoComplete="off" />
                </div>
                <div>
                    <FieldLabel htmlFor={id('port')}>Port</FieldLabel>
                    <input id={id('port')} type="number" min={1} max={65535} value={settings.smtpPort} onChange={e => onChange({ smtpPort: parseInt(e.target.value, 10) || 587 })} className="vora-input" />
                </div>
            </div>

            <fieldset>
                <legend className="mb-1.5 block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">Connection security</legend>
                <div className="flex flex-col gap-3 sm:flex-row">
                    {SECURITY_OPTIONS.map(opt => (
                        <label key={opt.value} className="flex cursor-pointer items-center gap-2">
                            <input type="radio" name={id('security')} value={opt.value} checked={tlsMode === opt.value} onChange={() => onTlsMode(opt.value)} className="cursor-pointer accent-[var(--vora-accent-500)]" />
                            <span className="text-sm text-[var(--vora-text-primary)]">{opt.label}</span>
                        </label>
                    ))}
                </div>
            </fieldset>

            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                <div>
                    <FieldLabel htmlFor={id('username')}>Username</FieldLabel>
                    <input id={id('username')} type="text" value={settings.smtpUsername ?? ''} onChange={e => onChange({ smtpUsername: e.target.value || null })} placeholder="you@example.com" className="vora-input" autoComplete="off" />
                </div>
                <div>
                    <FieldLabel htmlFor={id('password')}>Password</FieldLabel>
                    {password.clear ? (
                        <div className="flex h-[38px] items-center gap-3">
                            <span className="text-sm text-[var(--vora-danger-text)]">Will be cleared on save.</span>
                            <button type="button" onClick={() => onPassword({ clear: false })} className="cursor-pointer text-xs text-[var(--vora-accent-text)] hover:underline">Undo</button>
                        </div>
                    ) : passwordIsSet && !password.editing ? (
                        <div className="flex h-[38px] items-center gap-3">
                            <span className="text-sm text-[var(--vora-text-secondary)]">A password is saved.</span>
                            <button type="button" onClick={() => onPassword({ editing: true })} className="cursor-pointer text-xs text-[var(--vora-accent-text)] hover:underline">Change</button>
                            <button type="button" onClick={() => onPassword({ clear: true })} className="cursor-pointer text-xs text-[var(--vora-danger-text)] hover:underline">Clear</button>
                        </div>
                    ) : (
                        <div className="flex items-center gap-2">
                            <input
                                id={id('password')}
                                type="password"
                                value={password.value}
                                onChange={e => onPassword({ value: e.target.value })}
                                placeholder={passwordIsSet ? 'New password' : 'App password or SMTP password'}
                                className="vora-input flex-1"
                                autoComplete="new-password"
                            />
                            {passwordIsSet && (
                                <button type="button" onClick={() => onPassword({ editing: false, value: '' })} className="cursor-pointer text-xs text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)]">Cancel</button>
                            )}
                        </div>
                    )}
                    <FieldHint>Encrypted at rest using the server's data-protection keys.</FieldHint>
                </div>
            </div>

            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                <div>
                    <FieldLabel htmlFor={id('from-address')}>From address</FieldLabel>
                    <input id={id('from-address')} type="email" value={settings.smtpFromAddress ?? ''} onChange={e => onChange({ smtpFromAddress: e.target.value || null })} placeholder="noreply@example.com" className="vora-input" />
                </div>
                <div>
                    <FieldLabel htmlFor={id('from-name')}>From display name</FieldLabel>
                    <input id={id('from-name')} type="text" value={settings.smtpFromDisplayName ?? ''} onChange={e => onChange({ smtpFromDisplayName: e.target.value || null })} placeholder="Vora Server" className="vora-input" />
                </div>
            </div>

            <div>
                <FieldLabel htmlFor={id('public-url')}>Public base URL</FieldLabel>
                <input id={id('public-url')} type="url" value={settings.emailPublicBaseUrl ?? ''} onChange={e => onChange({ emailPublicBaseUrl: e.target.value || null })} placeholder="https://vora.example.com" className="vora-input" />
                <FieldHint>{publicUrlHint ?? 'Used to build the links in emails, such as password resets and invitations. Leave blank to use the address the request came from.'}</FieldHint>
            </div>
        </div>
    );
}
