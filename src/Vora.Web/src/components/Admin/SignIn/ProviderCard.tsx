import { useState } from 'react';
import HealthBadge from '../Primitives/HealthBadge';
import type { AuthProviderAdmin, LinkByEmailPolicy, NewUserPolicy, OidcTestResult } from '../../../api/Auth/signInProvidersAdminService';
import { copyText } from '../../../utils/copyText';

const NEW_USERS: Record<NewUserPolicy, string> = {
    FollowRegistrationMode: 'Follows the registration mode',
    IdpDecides: 'The provider decides (anyone it signs in)',
    ExistingOnly: 'Existing accounts only',
};

const LINKING: Record<LinkByEmailPolicy, string> = {
    ConfirmWithPassword: "Asks for the account's password once",
    Automatic: 'Links automatically',
    Never: 'Never by email',
};

interface ProviderCardProps {
    provider: AuthProviderAdmin;
    test: OidcTestResult | 'testing' | null;
    onEdit: () => void;
    onTest: () => void;
    onDelete: () => void;
}

export default function ProviderCard({ provider, test, onEdit, onTest, onDelete }: ProviderCardProps) {
    const [copied, setCopied] = useState(false);

    const copyRedirect = async () => {
        setCopied(await copyText(provider.redirectUri));
    };

    return (
        <article className="vora-card overflow-hidden" aria-label={provider.displayName}>
            <div className="flex flex-wrap items-center justify-between gap-3 border-b border-[var(--vora-border-subtle)] px-5 py-4 max-md:px-4">
                <div className="min-w-0">
                    <div className="flex items-center gap-2">
                        <h3 className="truncate text-sm font-semibold text-[var(--vora-text-primary)]">{provider.displayName}</h3>
                        <HealthBadge tone={provider.enabled ? 'ok' : 'neutral'} showDot={false}>{provider.enabled ? 'On' : 'Off'}</HealthBadge>
                    </div>
                    <p className="truncate font-mono text-xs text-[var(--vora-text-muted)]">{provider.issuer ?? 'No issuer yet'}</p>
                </div>
                <div className="flex shrink-0 flex-wrap items-center gap-2">
                    <button type="button" onClick={onTest} disabled={test === 'testing'} className="vora-button-secondary text-xs disabled:opacity-50">
                        {test === 'testing' ? 'Testing…' : 'Test connection'}
                    </button>
                    <button type="button" onClick={onEdit} className="vora-button-secondary text-xs">Edit</button>
                    <button type="button" onClick={onDelete} className="vora-button-secondary text-xs text-[var(--vora-danger-text)]">Remove</button>
                </div>
            </div>

            <dl className="grid gap-x-6 gap-y-2 px-5 py-4 text-sm max-md:px-4 md:grid-cols-2">
                <div className="min-w-0 md:col-span-2">
                    <dt className="text-[10px] font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">Redirect address</dt>
                    <dd className="flex items-center gap-2">
                        <code className="min-w-0 flex-1 truncate rounded bg-[var(--vora-bg-sunken)] px-2 py-1 text-xs text-[var(--vora-text-primary)]">{provider.redirectUri}</code>
                        <button type="button" onClick={copyRedirect} className="vora-button-secondary shrink-0 text-xs">{copied ? 'Copied' : 'Copy'}</button>
                    </dd>
                </div>
                <div>
                    <dt className="text-[10px] font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">New people</dt>
                    <dd className="text-[var(--vora-text-secondary)]">{NEW_USERS[provider.newUserPolicy]}</dd>
                </div>
                <div>
                    <dt className="text-[10px] font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">Existing accounts</dt>
                    <dd className="text-[var(--vora-text-secondary)]">{LINKING[provider.linkByEmail]}</dd>
                </div>
                {(provider.requiredGroup || provider.allowedEmailDomains.length > 0 || provider.adminGroup) && (
                    <div className="md:col-span-2 text-xs text-[var(--vora-text-muted)]">
                        {provider.requiredGroup && <span className="mr-4">Requires group <strong>{provider.requiredGroup}</strong></span>}
                        {provider.allowedEmailDomains.length > 0 && <span className="mr-4">Domains {provider.allowedEmailDomains.join(', ')}</span>}
                        {provider.adminGroup && <span>Group <strong>{provider.adminGroup}</strong> become administrators</span>}
                    </div>
                )}
            </dl>

            {test && test !== 'testing' && (
                <div
                    role={test.ok ? 'status' : 'alert'}
                    className={`mx-5 mb-4 rounded-[var(--vora-radius-md)] border p-3 text-sm max-md:mx-4 ${test.ok
                        ? 'border-[var(--vora-success-500)] bg-[var(--vora-success-soft)] text-[var(--vora-success-text)]'
                        : 'border-[var(--vora-danger-500)] bg-[var(--vora-danger-soft)] text-[var(--vora-danger-text)]'}`}
                >
                    <p className="font-semibold">{test.ok ? 'Connected' : 'Could not connect'}</p>
                    {test.message && <p className="mt-0.5">{test.message}</p>}
                    {test.ok && (
                        <ul className="mt-1.5 space-y-0.5 text-xs">
                            {test.authorizationEndpoint && <li>Sign-in page: <span className="font-mono">{test.authorizationEndpoint}</span></li>}
                            {test.tokenEndpoint && <li>Token address: <span className="font-mono">{test.tokenEndpoint}</span></li>}
                            <li>{test.signingKeyCount} signing {test.signingKeyCount === 1 ? 'key' : 'keys'} found{test.advertisesPkceS256 ? ', PKCE supported' : ', PKCE not advertised'}</li>
                        </ul>
                    )}
                    {test.warnings.length > 0 && (
                        <ul className="mt-1.5 list-disc space-y-0.5 pl-5 text-xs">
                            {test.warnings.map(w => <li key={w}>{w}</li>)}
                        </ul>
                    )}
                </div>
            )}
        </article>
    );
}
