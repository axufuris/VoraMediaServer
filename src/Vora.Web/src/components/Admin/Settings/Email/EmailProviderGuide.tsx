import { useState } from 'react';
import { EMAIL_PROVIDERS, findEmailProvider, type EmailProvider, type EmailProviderSmtp } from './emailProviders';
import type { TlsMode } from './smtpSettings';

const SECURITY_LABELS: Record<TlsMode, string> = { startTls: 'STARTTLS', implicitSsl: 'SSL/TLS', none: 'None' };

interface EmailProviderGuideProps {
    initialProviderId?: string | null;
    onApply?: (smtp: EmailProviderSmtp) => void;
}

function ProviderButton({ provider, selected, onSelect }: { provider: EmailProvider; selected: boolean; onSelect: () => void }) {
    return (
        <button
            type="button"
            aria-pressed={selected}
            onClick={onSelect}
            className={`cursor-pointer rounded-full border px-3 py-1 text-[13px] font-medium transition-colors ${selected
                ? 'border-[var(--vora-accent-500)] bg-[var(--vora-accent-soft)] text-[var(--vora-accent-text)]'
                : 'border-[var(--vora-border-strong)] text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-sunken)] hover:text-[var(--vora-text-primary)]'}`}
        >
            {provider.name}
        </button>
    );
}

function ProviderDetails({ provider, onApply }: { provider: EmailProvider; onApply?: (smtp: EmailProviderSmtp) => void }) {
    const [applied, setApplied] = useState(false);
    const smtp = provider.smtp;
    const rows: [string, string, boolean][] = provider.unsupported ? [] : [
        ...(smtp ? [['Server', smtp.host, true], ['Port', String(smtp.port), true], ['Security', SECURITY_LABELS[smtp.security], false]] as [string, string, boolean][] : []),
        ['Username', provider.username, false],
        ['Password', provider.password, false],
    ];
    return (
        <div className="flex flex-col gap-3 rounded-[var(--vora-radius-lg)] border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] p-4">
            {provider.unsupported && <p className="text-sm text-[var(--vora-warning-text)]">{provider.unsupported}</p>}
            {rows.length > 0 && (
                <dl className="grid gap-x-4 gap-y-1.5 text-sm sm:grid-cols-[max-content_minmax(0,1fr)]">
                    {rows.map(([label, value, mono]) => (
                        <div key={label} className="contents">
                            <dt className="text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] sm:pt-0.5">{label}</dt>
                            <dd className={`min-w-0 text-[var(--vora-text-primary)] ${mono ? 'break-all font-mono text-[13px]' : ''}`}>{value}</dd>
                        </div>
                    ))}
                </dl>
            )}
            {provider.notes.length > 0 && (
                <ul className="list-disc space-y-1 pl-5 text-[13px] text-[var(--vora-text-secondary)]">
                    {provider.notes.map(note => <li key={note}>{note}</li>)}
                </ul>
            )}
            {(provider.links.length > 0 || (onApply && smtp)) && (
                <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
                    {provider.links.map(link => (
                        <a key={link.url} href={link.url} target="_blank" rel="noopener noreferrer" className="text-[13px] font-semibold text-[var(--vora-accent-text)] hover:underline">
                            {link.label} ↗
                        </a>
                    ))}
                    {onApply && smtp && (
                        <button type="button" onClick={() => { onApply(smtp); setApplied(true); }} className="vora-button-secondary ml-auto !px-3 !py-1.5 text-xs">
                            {applied ? 'Filled in' : 'Fill in server settings'}
                        </button>
                    )}
                </div>
            )}
            {applied && <p role="status" className="text-[13px] text-[var(--vora-success-text)]">Server, port and security are filled in. Add the username and password below.</p>}
        </div>
    );
}

export default function EmailProviderGuide({ initialProviderId = null, onApply }: EmailProviderGuideProps) {
    const [selectedId, setSelectedId] = useState<string | null>(initialProviderId);
    const provider = selectedId ? findEmailProvider(selectedId) : undefined;
    const select = (id: string) => setSelectedId(current => current === id ? null : id);
    const mailboxes = EMAIL_PROVIDERS.filter(p => p.kind === 'mailbox');
    const services = EMAIL_PROVIDERS.filter(p => p.kind !== 'mailbox');

    return (
        <div className="flex flex-col gap-3">
            <div>
                <p className="text-sm font-semibold text-[var(--vora-text-primary)]">Which email service will Vora send from?</p>
                <p className="mt-0.5 text-xs text-[var(--vora-text-muted)]">
                    Pick one to see what to enter and where to get it. An app password on an address you already have is quickest; a sending service suits a server with lots of users.
                </p>
            </div>
            <div role="group" aria-label="Email accounts" className="flex flex-wrap items-center gap-1.5">
                <span className="mr-1 text-[10px] font-bold uppercase tracking-[0.12em] text-[var(--vora-text-muted)]">Email accounts</span>
                {mailboxes.map(p => <ProviderButton key={p.id} provider={p} selected={p.id === selectedId} onSelect={() => select(p.id)} />)}
            </div>
            <div role="group" aria-label="Sending services" className="flex flex-wrap items-center gap-1.5">
                <span className="mr-1 text-[10px] font-bold uppercase tracking-[0.12em] text-[var(--vora-text-muted)]">Sending services</span>
                {services.map(p => <ProviderButton key={p.id} provider={p} selected={p.id === selectedId} onSelect={() => select(p.id)} />)}
            </div>
            {provider && <ProviderDetails key={provider.id} provider={provider} onApply={onApply} />}
        </div>
    );
}
