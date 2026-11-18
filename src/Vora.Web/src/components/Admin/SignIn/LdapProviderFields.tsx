import { useState } from 'react';
import { ChoiceCard, CheckRow, FieldLabel, Hint, RecommendedBadge } from '../../../pages/Admin/SetupGuide/SetupParts';
import type { AuthProviderAdmin, LdapSecurity } from '../../../api/Auth/signInProvidersAdminService';
import { DEFAULT_LDAP_PORTS, type ProviderDraft } from './providerDraft';

interface LdapProviderFieldsProps {
    draft: ProviderDraft;
    provider: AuthProviderAdmin | null;
    set: <K extends keyof ProviderDraft>(key: K, value: ProviderDraft[K]) => void;
}

const SECURITY_CHOICES: { value: LdapSecurity; title: string; description: string; badge?: string }[] = [
    {
        value: 'Ldaps',
        title: 'LDAPS',
        description: 'Encrypted from the first byte, usually on port 636. What nearly every directory offers.',
        badge: 'Recommended',
    },
    {
        value: 'StartTls',
        title: 'StartTLS',
        description: 'Starts on the plain port, usually 389, and switches to an encrypted connection before any password is sent.',
    },
    {
        value: 'None',
        title: 'No encryption',
        description: 'Passwords cross the network in the clear. Only for a directory on a network you trust, such as a server on your home network.',
    },
];

export default function LdapProviderFields({ draft, provider, set }: LdapProviderFieldsProps) {
    const [showAdvanced, setShowAdvanced] = useState(false);
    const defaultPort = DEFAULT_LDAP_PORTS[draft.ldapSecurity];

    return (
        <>
            <section className="space-y-3">
                <h3 className="text-sm font-semibold text-[var(--vora-text-primary)]">Connection</h3>
                <div className="grid gap-4 md:grid-cols-3">
                    <div className="md:col-span-2">
                        <FieldLabel htmlFor="provider-ldap-host">Host</FieldLabel>
                        <input
                            id="provider-ldap-host"
                            value={draft.ldapHost}
                            onChange={e => set('ldapHost', e.target.value)}
                            className="vora-input w-full font-mono"
                            placeholder="ldap.example.com"
                            autoComplete="off"
                            spellCheck={false}
                        />
                        <Hint>The name or address of the directory server, without ldap:// or a port.</Hint>
                    </div>
                    <div>
                        <FieldLabel htmlFor="provider-ldap-port">Port (optional)</FieldLabel>
                        <input
                            id="provider-ldap-port"
                            value={draft.ldapPort}
                            onChange={e => set('ldapPort', e.target.value.replace(/\D/g, '').slice(0, 5))}
                            inputMode="numeric"
                            className="vora-input w-full font-mono"
                            placeholder={String(defaultPort)}
                        />
                        <Hint>Empty means {defaultPort}.</Hint>
                    </div>
                </div>

                <div role="radiogroup" aria-label="Encryption" className="grid gap-2.5 md:grid-cols-3">
                    {SECURITY_CHOICES.map(choice => (
                        <ChoiceCard
                            key={choice.value}
                            selected={draft.ldapSecurity === choice.value}
                            onSelect={() => set('ldapSecurity', choice.value)}
                            title={choice.title}
                            description={choice.description}
                            badge={choice.badge ? <RecommendedBadge>{choice.badge}</RecommendedBadge> : undefined}
                        />
                    ))}
                </div>

                {draft.ldapSecurity === 'None' ? (
                    <div className="space-y-2">
                        <p role="note" className="rounded-[var(--vora-radius-md)] border border-[var(--vora-warning-500)] bg-[var(--vora-warning-soft)] p-3 text-sm text-[var(--vora-warning-text)]">
                            Without encryption the service account password and every password people type here can be read by anything on the path between Vora and the directory. Vora only allows this for a host on a private network.
                        </p>
                        <CheckRow
                            id="provider-ldap-plain"
                            checked={draft.plainAcknowledged}
                            onChange={v => set('plainAcknowledged', v)}
                            label="I understand, and this directory is on a network I trust"
                        />
                    </div>
                ) : (
                    <div>
                        <FieldLabel htmlFor="provider-ldap-ca">Certificate authority (optional)</FieldLabel>
                        <textarea
                            id="provider-ldap-ca"
                            value={draft.ldapCaCertificatePem}
                            onChange={e => set('ldapCaCertificatePem', e.target.value)}
                            rows={4}
                            spellCheck={false}
                            className="vora-input w-full font-mono text-xs"
                            placeholder={'-----BEGIN CERTIFICATE-----\n…\n-----END CERTIFICATE-----'}
                        />
                        <Hint>Paste the PEM certificate of your own authority when the directory's certificate comes from it. Empty trusts the same authorities this server's system does. The certificate must still be valid for the host above.</Hint>
                    </div>
                )}
            </section>

            <section className="space-y-3">
                <h3 className="text-sm font-semibold text-[var(--vora-text-primary)]">Service account</h3>
                <div className="grid gap-4 md:grid-cols-2">
                    <div>
                        <FieldLabel htmlFor="provider-ldap-bind-dn">Bind DN</FieldLabel>
                        <input
                            id="provider-ldap-bind-dn"
                            value={draft.ldapBindDn}
                            onChange={e => set('ldapBindDn', e.target.value)}
                            className="vora-input w-full font-mono"
                            placeholder="cn=vora,ou=service,dc=example,dc=com"
                            autoComplete="off"
                            spellCheck={false}
                        />
                        <Hint>A read-only account Vora uses to find people. Empty searches anonymously, which most directories refuse.</Hint>
                    </div>
                    <div>
                        <FieldLabel htmlFor="provider-ldap-bind-password">Bind password</FieldLabel>
                        <input
                            id="provider-ldap-bind-password"
                            type="password"
                            value={draft.ldapBindPassword}
                            onChange={e => set('ldapBindPassword', e.target.value)}
                            className="vora-input w-full font-mono"
                            autoComplete="new-password"
                            placeholder={provider?.hasBindPassword ? 'A password is saved. Type to replace it.' : ''}
                        />
                        <Hint>
                            {provider?.hasBindPassword
                                ? 'Saved passwords are never shown again. Leave this empty to keep it.'
                                : 'Stored encrypted on this server and never shown again.'}
                        </Hint>
                        {provider?.hasBindPassword && (
                            <CheckRow id="provider-ldap-clear-password" checked={draft.clearBindPassword} onChange={v => set('clearBindPassword', v)} label="Remove the saved password" />
                        )}
                    </div>
                </div>
            </section>

            <section className="space-y-3">
                <h3 className="text-sm font-semibold text-[var(--vora-text-primary)]">People</h3>
                <div className="grid gap-4">
                    <div>
                        <FieldLabel htmlFor="provider-ldap-base">User search base</FieldLabel>
                        <input
                            id="provider-ldap-base"
                            value={draft.ldapUserBaseDn}
                            onChange={e => set('ldapUserBaseDn', e.target.value)}
                            className="vora-input w-full font-mono"
                            placeholder="ou=people,dc=example,dc=com"
                            autoComplete="off"
                            spellCheck={false}
                        />
                        <Hint>The branch of the directory that holds the people. Everything below it is searched.</Hint>
                    </div>
                    <div>
                        <FieldLabel htmlFor="provider-ldap-filter">User filter</FieldLabel>
                        <input
                            id="provider-ldap-filter"
                            value={draft.ldapUserFilter}
                            onChange={e => set('ldapUserFilter', e.target.value)}
                            className="vora-input w-full font-mono"
                            placeholder="(&(objectClass=person)(uid={username}))"
                            autoComplete="off"
                            spellCheck={false}
                        />
                        <Hint>Finds one person. <code>{'{username}'}</code> becomes what was typed on the sign-in page, with special characters escaped. Match several attributes with <code>(|(uid={'{username}'})(mail={'{username}'}))</code> so people can use either. A filter that finds more than one entry signs nobody in.</Hint>
                    </div>
                </div>
                <p className="text-xs text-[var(--vora-text-muted)]">Every person needs an email address in the directory. Vora makes an account from it, and refuses a person who has none.</p>
            </section>

            <section className="space-y-3">
                <h3 className="text-sm font-semibold text-[var(--vora-text-primary)]">Groups (optional)</h3>
                <p className="text-xs text-[var(--vora-text-muted)]">Groups are only read when you limit sign-in to a group or make a group administrators, further down. Vora reads the group attribute on the person and, when you fill them in, also searches for groups that list the person.</p>
                <div className="grid gap-4 md:grid-cols-2">
                    <div>
                        <FieldLabel htmlFor="provider-ldap-group-base">Group search base</FieldLabel>
                        <input
                            id="provider-ldap-group-base"
                            value={draft.ldapGroupBaseDn}
                            onChange={e => set('ldapGroupBaseDn', e.target.value)}
                            className="vora-input w-full font-mono"
                            placeholder="ou=groups,dc=example,dc=com"
                            autoComplete="off"
                            spellCheck={false}
                        />
                    </div>
                    <div>
                        <FieldLabel htmlFor="provider-ldap-group-filter">Group filter</FieldLabel>
                        <input
                            id="provider-ldap-group-filter"
                            value={draft.ldapGroupFilter}
                            onChange={e => set('ldapGroupFilter', e.target.value)}
                            className="vora-input w-full font-mono"
                            placeholder="(&(objectClass=groupOfNames)(member={dn}))"
                            autoComplete="off"
                            spellCheck={false}
                        />
                        <Hint><code>{'{dn}'}</code> is the person's entry and <code>{'{username}'}</code> what they typed. Use it when the directory has no memberOf attribute.</Hint>
                    </div>
                </div>
            </section>

            <section className="space-y-3">
                <button type="button" onClick={() => setShowAdvanced(v => !v)} aria-expanded={showAdvanced} className="cursor-pointer text-sm font-semibold text-[var(--vora-accent-text)] hover:underline">
                    {showAdvanced ? 'Hide attribute names' : 'Show attribute names'}
                </button>
                {showAdvanced && (
                    <div className="grid gap-4 md:grid-cols-4">
                        <div>
                            <FieldLabel htmlFor="provider-ldap-mail">Email</FieldLabel>
                            <input id="provider-ldap-mail" value={draft.ldapMailAttribute} onChange={e => set('ldapMailAttribute', e.target.value)} className="vora-input w-full font-mono" />
                        </div>
                        <div>
                            <FieldLabel htmlFor="provider-ldap-display-name">Display name</FieldLabel>
                            <input id="provider-ldap-display-name" value={draft.ldapDisplayNameAttribute} onChange={e => set('ldapDisplayNameAttribute', e.target.value)} className="vora-input w-full font-mono" />
                        </div>
                        <div>
                            <FieldLabel htmlFor="provider-ldap-unique-id">Unique id</FieldLabel>
                            <input id="provider-ldap-unique-id" value={draft.ldapUniqueIdAttribute} onChange={e => set('ldapUniqueIdAttribute', e.target.value)} className="vora-input w-full font-mono" />
                        </div>
                        <div>
                            <FieldLabel htmlFor="provider-ldap-groups">Groups</FieldLabel>
                            <input id="provider-ldap-groups" value={draft.ldapGroupAttribute} onChange={e => set('ldapGroupAttribute', e.target.value)} className="vora-input w-full font-mono" />
                        </div>
                        <p className="text-xs text-[var(--vora-text-muted)] md:col-span-4">
                            The unique id ties a sign-in to a person for good, so renaming them in the directory keeps their account. Use entryUUID for OpenLDAP, uuid for LLDAP, uid for Authentik, ipaUniqueID for FreeIPA and objectGUID for Active Directory. If an entry has none, its distinguished name is used.
                        </p>
                    </div>
                )}
            </section>
        </>
    );
}
