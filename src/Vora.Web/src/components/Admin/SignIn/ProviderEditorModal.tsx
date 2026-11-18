import { useState } from 'react';
import { Modal, ModalHeader } from '../../Common/Modal';
import { ChoiceCard, CheckRow, FieldLabel, Hint, RecommendedBadge, SwitchRow } from '../../../pages/Admin/SetupGuide/SetupParts';
import type { AuthProviderAdmin, LinkByEmailPolicy, NewUserPolicy, SaveAuthProvider } from '../../../api/Auth/signInProvidersAdminService';
import { resolveReason } from '../../../utils/apiError';
import { copyText } from '../../../utils/copyText';
import { blankDraft, draftFromProvider, redirectUriFor, toSaveRequest, validateDraft, type ProviderDraft } from './providerDraft';
import type { ProviderPreset } from './providerPresets';
import LdapProviderFields from './LdapProviderFields';

interface ProviderEditorModalProps {
    provider: AuthProviderAdmin | null;
    preset: ProviderPreset | null;
    effectiveBaseUrl: string;
    sortOrder: number;
    onSave: (request: SaveAuthProvider) => Promise<void>;
    onClose: () => void;
}

const NEW_USER_CHOICES: { value: NewUserPolicy; title: string; description: string; badge?: string }[] = [
    {
        value: 'FollowRegistrationMode',
        title: 'Follow the registration mode',
        description: 'New people can make an account only the way your sign-up setting allows: open sign-up, an invite PIN or an emailed invitation. When sign-ups are disabled, only people who already have an account can sign in.',
        badge: 'Recommended',
    },
    {
        value: 'IdpDecides',
        title: 'The identity provider decides',
        description: 'Anyone this provider signs in gets an account, whatever the registration mode. Use it only when the provider already controls who has access, and narrow it below with an email domain or a group.',
        badge: 'Opens sign-up',
    },
    {
        value: 'ExistingOnly',
        title: 'Existing accounts only',
        description: 'This provider never creates accounts. People who already have one can link it, from Account settings or the first time they sign in.',
    },
];

const DIRECTORY_NEW_USER_DESCRIPTIONS: Record<NewUserPolicy, string> = {
    FollowRegistrationMode: 'New people can make an account only the way your sign-up setting allows: open sign-up, an invite PIN or an emailed invitation. When sign-ups are disabled, only people who already have an account can sign in.',
    IdpDecides: 'Anyone the directory accepts gets an account, whatever the registration mode. Use it only when the directory already controls who has access, and narrow it below with an email domain or a group.',
    ExistingOnly: 'The directory never creates accounts. People who already have one are matched to it by their email address.',
};

const DIRECTORY_AUTOMATIC_LINK = 'Link to the account with the same email address without asking. Only for a directory you run and trust: anyone who can set the mail attribute there could reach that account.';

const LINK_CHOICES: { value: LinkByEmailPolicy; title: string; description: string; badge?: string }[] = [
    {
        value: 'ConfirmWithPassword',
        title: "Ask for the account's password once",
        description: "When the provider's email matches an account here, the person proves it is theirs with that account's password, once. Nothing is linked silently.",
        badge: 'Recommended',
    },
    {
        value: 'Automatic',
        title: 'Link automatically',
        description: 'When the provider says the email is verified, link to the account with that email without asking. Only for a provider you run and trust: anyone who can set an email there could reach that account.',
    },
    {
        value: 'Never',
        title: 'Never link by email',
        description: 'Existing accounts are linked only by the person, from Account settings while signed in.',
    },
];

export default function ProviderEditorModal({ provider, preset, effectiveBaseUrl, sortOrder, onSave, onClose }: ProviderEditorModalProps) {
    const [draft, setDraft] = useState<ProviderDraft>(() => provider ? draftFromProvider(provider) : blankDraft(preset?.draft));
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [copied, setCopied] = useState(false);
    const [showClaims, setShowClaims] = useState(false);

    const set = <K extends keyof ProviderDraft>(key: K, value: ProviderDraft[K]) => setDraft(current => ({ ...current, [key]: value }));
    const isLdap = draft.type === 'Ldap';
    const redirectUri = provider ? provider.redirectUri : redirectUriFor(effectiveBaseUrl, draft.slug);

    const submit = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        if (saving) return;
        const problem = validateDraft(draft);
        if (problem) {
            setError(problem);
            return;
        }
        setSaving(true);
        setError(null);
        try {
            await onSave(toSaveRequest(draft, provider?.sortOrder ?? sortOrder));
        } catch (err) {
            setError(resolveReason(err) ?? 'Could not save the provider.');
            setSaving(false);
        }
    };

    const copyRedirect = async () => {
        setCopied(await copyText(redirectUri));
    };

    return (
        <Modal isOpen onClose={onClose} size="3xl" zIndex="z-[200]" surface="light" cardClassName="flex flex-col max-h-[90vh]">
            <ModalHeader title={provider ? `Edit ${provider.displayName}` : `Add ${preset?.label ?? 'a provider'}`} onClose={onClose} surface="light" />
            <form onSubmit={submit} className="flex min-h-0 flex-1 flex-col">
                <div className="min-h-0 flex-1 space-y-6 overflow-y-auto p-6 max-md:p-4">
                    {preset && !provider && (
                        <p className="rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] p-3 text-sm text-[var(--vora-text-secondary)]">{preset.steps}</p>
                    )}

                    {!isLdap && (
                    <section className="space-y-2">
                        <FieldLabel htmlFor="provider-redirect">Redirect address to register at the provider</FieldLabel>
                        <div className="flex gap-2">
                            <input id="provider-redirect" readOnly value={redirectUri} className="vora-input w-full font-mono text-sm" onFocus={e => e.currentTarget.select()} />
                            <button type="button" onClick={copyRedirect} className="vora-button-secondary shrink-0">{copied ? 'Copied' : 'Copy'}</button>
                        </div>
                        <Hint>It has to match what the provider has character for character. If Vora sits behind a reverse proxy, set the public address on the previous page first.</Hint>
                    </section>
                    )}

                    <section className="grid gap-4 md:grid-cols-2">
                        <div>
                            <FieldLabel htmlFor="provider-name">{isLdap ? 'Name' : 'Button name'}</FieldLabel>
                            <input id="provider-name" value={draft.displayName} onChange={e => set('displayName', e.target.value)} maxLength={64} className="vora-input w-full" placeholder={isLdap ? 'e.g. Company directory' : 'e.g. Authentik'} />
                            <Hint>{isLdap ? 'Shown when someone is asked to link their account, such as "Link Company directory". Directory sign-in has no button: people use the email or username box.' : 'Shown on the sign-in page as "Continue with …".'}</Hint>
                        </div>
                        <div>
                            <FieldLabel htmlFor="provider-slug">Address name</FieldLabel>
                            <input id="provider-slug" value={draft.slug} onChange={e => set('slug', e.target.value.toLowerCase())} maxLength={64} className="vora-input w-full font-mono" placeholder={isLdap ? 'directory' : 'authentik'} />
                            <Hint>{isLdap ? 'A short id for this directory. Lower-case letters, digits and hyphens.' : 'Part of the redirect address. Lower-case letters, digits and hyphens. Changing it changes the address you registered.'}</Hint>
                        </div>
                    </section>

                    {isLdap && <LdapProviderFields draft={draft} provider={provider} set={set} />}

                    {!isLdap && (
                    <section className="grid gap-4 md:grid-cols-2">
                        <div className="md:col-span-2">
                            <FieldLabel htmlFor="provider-issuer">Issuer address</FieldLabel>
                            <input id="provider-issuer" value={draft.issuer} onChange={e => set('issuer', e.target.value)} className="vora-input w-full font-mono" placeholder="https://auth.example.com/application/o/vora/" />
                            <Hint>The provider's OpenID Connect issuer. Vora reads its settings from this address plus <code>/.well-known/openid-configuration</code>.</Hint>
                        </div>
                        <div>
                            <FieldLabel htmlFor="provider-client-id">Client id</FieldLabel>
                            <input id="provider-client-id" value={draft.clientId} onChange={e => set('clientId', e.target.value)} className="vora-input w-full font-mono" autoComplete="off" />
                        </div>
                        <div>
                            <FieldLabel htmlFor="provider-client-secret">Client secret</FieldLabel>
                            <input
                                id="provider-client-secret"
                                type="password"
                                value={draft.clientSecret}
                                onChange={e => set('clientSecret', e.target.value)}
                                className="vora-input w-full font-mono"
                                autoComplete="new-password"
                                placeholder={provider?.hasSecret ? 'A secret is saved. Type to replace it.' : ''}
                            />
                            <Hint>
                                {provider?.hasSecret
                                    ? 'Saved secrets are never shown again. Leave this empty to keep it.'
                                    : 'Leave empty for a provider that does not use a secret.'}
                            </Hint>
                            {provider?.hasSecret && (
                                <CheckRow id="provider-clear-secret" checked={draft.clearSecret} onChange={v => set('clearSecret', v)} label="Remove the saved secret" />
                            )}
                        </div>
                        <div className="md:col-span-2">
                            <FieldLabel htmlFor="provider-scopes">Scopes</FieldLabel>
                            <input id="provider-scopes" value={draft.scopes} onChange={e => set('scopes', e.target.value)} className="vora-input w-full font-mono" />
                            <Hint>Must include <code>openid</code>. Add <code>groups</code> if you limit sign-in by group.</Hint>
                        </div>
                    </section>
                    )}

                    <section className="space-y-3">
                        <h3 className="text-sm font-semibold text-[var(--vora-text-primary)]">New people</h3>
                        <div role="radiogroup" aria-label="New people" className="grid gap-2.5 md:grid-cols-3">
                            {NEW_USER_CHOICES.map(choice => (
                                <ChoiceCard
                                    key={choice.value}
                                    selected={draft.newUserPolicy === choice.value}
                                    onSelect={() => set('newUserPolicy', choice.value)}
                                    title={choice.title}
                                    description={isLdap ? DIRECTORY_NEW_USER_DESCRIPTIONS[choice.value] : choice.description}
                                    badge={choice.badge ? <RecommendedBadge>{choice.badge}</RecommendedBadge> : undefined}
                                />
                            ))}
                        </div>
                        {draft.newUserPolicy === 'IdpDecides' && (
                            <p role="note" className="rounded-[var(--vora-radius-md)] border border-[var(--vora-warning-500)] bg-[var(--vora-warning-soft)] p-3 text-sm text-[var(--vora-warning-text)]">
                                With this choice anyone the {isLdap ? 'directory accepts' : 'provider signs in'} can create an account on this server, even when sign-ups are closed. Limit it with an email domain or a required group below.
                            </p>
                        )}
                    </section>

                    <section className="space-y-3">
                        <h3 className="text-sm font-semibold text-[var(--vora-text-primary)]">When the email matches an account that already exists</h3>
                        <div role="radiogroup" aria-label="Linking by email" className="grid gap-2.5 md:grid-cols-3">
                            {LINK_CHOICES.map(choice => (
                                <ChoiceCard
                                    key={choice.value}
                                    selected={draft.linkByEmail === choice.value}
                                    onSelect={() => set('linkByEmail', choice.value)}
                                    title={choice.title}
                                    description={isLdap && choice.value === 'Automatic' ? DIRECTORY_AUTOMATIC_LINK : choice.description}
                                    badge={choice.badge ? <RecommendedBadge>{choice.badge}</RecommendedBadge> : undefined}
                                />
                            ))}
                        </div>
                        <Hint>{isLdap
                            ? 'The directory is the authority on its email addresses, so they count as verified. Someone who can set their own mail attribute there could claim an address that is not theirs, so only link automatically for a directory you control.'
                            : 'An email the provider has not verified is never used to link, whatever you pick.'}</Hint>
                    </section>

                    <section className="grid gap-4 md:grid-cols-2">
                        <div>
                            <FieldLabel htmlFor="provider-domains">Allowed email domains (optional)</FieldLabel>
                            <input id="provider-domains" value={draft.allowedEmailDomains} onChange={e => set('allowedEmailDomains', e.target.value)} className="vora-input w-full" placeholder="example.com, example.org" />
                            <Hint>When set, only people whose email ends in one of these can sign in, linked accounts included.</Hint>
                        </div>
                        <div>
                            <FieldLabel htmlFor="provider-required-group">Required group (optional)</FieldLabel>
                            <input id="provider-required-group" value={draft.requiredGroup} onChange={e => set('requiredGroup', e.target.value)} className="vora-input w-full" placeholder="vora-users" />
                            <Hint>{isLdap
                                ? 'When set, only members of this group can sign in. Give the group\'s name or its full distinguished name.'
                                : 'When set, only members of this group can sign in. The provider has to send groups in the token.'}</Hint>
                        </div>
                        <div className="md:col-span-2 space-y-2">
                            <CheckRow
                                id="provider-admin-group-on"
                                checked={draft.adminGroupOn}
                                onChange={v => set('adminGroupOn', v)}
                                label="Make members of a group administrators of this server"
                                hint="Off by default. Nobody becomes an administrator through a provider unless you turn this on. It promotes on sign-in and never takes admin away."
                            />
                            {draft.adminGroupOn && (
                                <div>
                                    <FieldLabel htmlFor="provider-admin-group">Administrator group</FieldLabel>
                                    <input id="provider-admin-group" value={draft.adminGroup} onChange={e => set('adminGroup', e.target.value)} className="vora-input w-full" placeholder="vora-admins" />
                                </div>
                            )}
                        </div>
                    </section>

                    {!isLdap && (
                    <section className="space-y-3">
                        <button type="button" onClick={() => setShowClaims(v => !v)} aria-expanded={showClaims} className="cursor-pointer text-sm font-semibold text-[var(--vora-accent-text)] hover:underline">
                            {showClaims ? 'Hide advanced settings' : 'Show advanced settings'}
                        </button>
                        {showClaims && (
                            <div className="grid gap-4 md:grid-cols-3">
                                <div>
                                    <FieldLabel htmlFor="provider-email-claim">Email claim</FieldLabel>
                                    <input id="provider-email-claim" value={draft.emailClaim} onChange={e => set('emailClaim', e.target.value)} className="vora-input w-full font-mono" />
                                </div>
                                <div>
                                    <FieldLabel htmlFor="provider-name-claim">Name claim</FieldLabel>
                                    <input id="provider-name-claim" value={draft.nameClaim} onChange={e => set('nameClaim', e.target.value)} className="vora-input w-full font-mono" />
                                </div>
                                <div>
                                    <FieldLabel htmlFor="provider-groups-claim">Groups claim</FieldLabel>
                                    <input id="provider-groups-claim" value={draft.groupsClaim} onChange={e => set('groupsClaim', e.target.value)} className="vora-input w-full font-mono" />
                                </div>
                                <div className="md:col-span-3">
                                    <FieldLabel htmlFor="provider-icon">Button icon address (optional)</FieldLabel>
                                    <input id="provider-icon" value={draft.iconUrl} onChange={e => set('iconUrl', e.target.value)} className="vora-input w-full" placeholder="https://example.com/logo.svg" />
                                </div>
                            </div>
                        )}
                    </section>
                    )}

                    <section>
                        <SwitchRow
                            id="provider-enabled"
                            label="Turned on"
                            description={isLdap
                                ? 'People can sign in with their directory username or email on the sign-in page. It needs the host, the user search base and the user filter.'
                                : 'Shows the button on the sign-in page. It needs an issuer and a client id.'}
                            checked={draft.enabled}
                            onChange={v => set('enabled', v)}
                        />
                    </section>

                    {error && <p role="alert" className="text-sm text-[var(--vora-danger-text)]">{error}</p>}
                </div>
                <div className="flex justify-end gap-2 border-t border-[var(--vora-border-subtle)] p-4">
                    <button type="button" onClick={onClose} className="vora-button-secondary">Cancel</button>
                    <button type="submit" disabled={saving} className="vora-button-primary disabled:opacity-50">{saving ? 'Saving…' : 'Save provider'}</button>
                </div>
            </form>
        </Modal>
    );
}
