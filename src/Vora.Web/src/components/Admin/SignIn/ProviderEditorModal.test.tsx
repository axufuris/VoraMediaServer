import { describe, it, expect, vi, afterEach } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import ProviderEditorModal from './ProviderEditorModal';
import { PROVIDER_PRESETS } from './providerPresets';
import { noDirectoryFields, savedDirectory } from '../../../test/signInFixtures';
import type { AuthProviderAdmin, SaveAuthProvider } from '../../../api/Auth/signInProvidersAdminService';

const saved: AuthProviderAdmin = {
    ...noDirectoryFields,
    id: 'p1', type: 'Oidc', slug: 'authentik', displayName: 'Authentik', enabled: true, iconUrl: null, sortOrder: 2,
    issuer: 'https://auth.example.com/application/o/vora/', clientId: 'client-1', hasSecret: true, scopes: 'openid email profile',
    emailClaim: 'email', nameClaim: 'name', groupsClaim: 'groups', newUserPolicy: 'FollowRegistrationMode', linkByEmail: 'ConfirmWithPassword',
    allowedEmailDomains: [], requiredGroup: null, adminGroup: null, redirectUri: 'https://vora.example.com/api/auth/oidc/authentik/callback',
};

const open = (provider: AuthProviderAdmin | null, onSave = vi.fn<(request: SaveAuthProvider) => Promise<void>>().mockResolvedValue(), preset = PROVIDER_PRESETS[0]) => {
    render(
        <ProviderEditorModal
            provider={provider}
            preset={provider ? null : preset}
            effectiveBaseUrl="https://vora.example.com"
            sortOrder={5}
            onSave={onSave}
            onClose={vi.fn()}
        />,
    );
    return onSave;
};

describe('ProviderEditorModal', () => {
    afterEach(cleanup);

    it('never shows a saved secret and leaves it alone when the field stays empty', async () => {
        const onSave = open(saved);

        const secret = screen.getByLabelText('Client secret');
        expect(secret).toHaveValue('');
        expect(secret).toHaveAttribute('type', 'password');
        expect(secret).toHaveAttribute('placeholder', expect.stringMatching(/secret is saved/));

        fireEvent.click(screen.getByRole('button', { name: 'Save provider' }));

        await waitFor(() => expect(onSave).toHaveBeenCalled());
        const request = onSave.mock.calls[0][0];
        expect('clientSecret' in request).toBe(false);
        expect('clearClientSecret' in request).toBe(false);
        expect(request.sortOrder).toBe(2);
    });

    it('replaces the secret only with what is typed', async () => {
        const onSave = open(saved);

        fireEvent.change(screen.getByLabelText('Client secret'), { target: { value: 's3cret' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save provider' }));

        await waitFor(() => expect(onSave).toHaveBeenCalled());
        expect(onSave.mock.calls[0][0].clientSecret).toBe('s3cret');
    });

    it('removes the secret when asked to', async () => {
        const onSave = open(saved);

        fireEvent.click(screen.getByLabelText('Remove the saved secret'));
        fireEvent.click(screen.getByRole('button', { name: 'Save provider' }));

        await waitFor(() => expect(onSave).toHaveBeenCalled());
        expect(onSave.mock.calls[0][0].clearClientSecret).toBe(true);
    });

    it('offers no remove-secret option when none is saved', () => {
        open({ ...saved, hasSecret: false });

        expect(screen.queryByLabelText('Remove the saved secret')).not.toBeInTheDocument();
        expect(screen.getByLabelText('Client secret')).toHaveAttribute('placeholder', '');
    });

    it('shows the exact redirect address and updates it as the address name changes', () => {
        open(null);

        const redirect = screen.getByLabelText('Redirect address to register at the provider');
        expect(redirect).toHaveValue('https://vora.example.com/api/auth/oidc/sso/callback');

        fireEvent.change(screen.getByLabelText('Address name'), { target: { value: 'Auth-1' } });

        expect(redirect).toHaveValue('https://vora.example.com/api/auth/oidc/auth-1/callback');
    });

    it('starts a Google provider from its preset', () => {
        const google = PROVIDER_PRESETS.find(p => p.id === 'google');
        if (!google) throw new Error('missing preset');
        open(null, undefined, google);

        expect(screen.getByLabelText('Button name')).toHaveValue('Google');
        expect(screen.getByLabelText('Issuer address')).toHaveValue('https://accounts.google.com');
        expect(screen.getByText(/OAuth client ID of type Web application/)).toBeInTheDocument();
    });

    it('does not save an invalid form and says what is wrong', async () => {
        const onSave = open(null);

        fireEvent.change(screen.getByLabelText('Address name'), { target: { value: 'bad name' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save provider' }));

        expect(await screen.findByRole('alert')).toHaveTextContent(/address name may use/);
        expect(onSave).not.toHaveBeenCalled();
    });

    it("shows the server's reason when saving fails", async () => {
        const onSave = vi.fn<(request: SaveAuthProvider) => Promise<void>>().mockRejectedValue(new Error('boom'));
        open(saved, onSave);

        fireEvent.click(screen.getByRole('button', { name: 'Save provider' }));

        expect(await screen.findByRole('alert')).toHaveTextContent('boom');
    });

    it('defaults to following the registration mode, asking for the password once, and no administrator mapping', async () => {
        const onSave = open(null);

        fireEvent.change(screen.getByLabelText('Button name'), { target: { value: 'Single sign-on' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save provider' }));

        await waitFor(() => expect(onSave).toHaveBeenCalled());
        const request = onSave.mock.calls[0][0];
        expect(request.newUserPolicy).toBe('FollowRegistrationMode');
        expect(request.linkByEmail).toBe('ConfirmWithPassword');
        expect(request.adminGroup).toBeNull();
        expect(request.enabled).toBe(false);
        expect(screen.queryByLabelText('Administrator group')).not.toBeInTheDocument();
    });

    it('warns when the provider is allowed to decide who gets an account', () => {
        open(null);

        expect(screen.queryByRole('note')).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('radio', { name: /The identity provider decides/ }));

        expect(screen.getByRole('note')).toHaveTextContent(/even when sign-ups are closed/);
    });

    it('asks for the group only once administrator promotion is turned on', async () => {
        const onSave = open(null);

        fireEvent.click(screen.getByLabelText(/Make members of a group administrators/));
        expect(screen.getByLabelText('Administrator group')).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Save provider' }));
        expect(await screen.findByRole('alert')).toHaveTextContent(/group whose members become administrators/);
        expect(onSave).not.toHaveBeenCalled();

        fireEvent.change(screen.getByLabelText('Administrator group'), { target: { value: 'vora-admins' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save provider' }));

        await waitFor(() => expect(onSave).toHaveBeenCalled());
        expect(onSave.mock.calls[0][0].adminGroup).toBe('vora-admins');
    });
});

describe('ProviderEditorModal for a directory', () => {
    afterEach(cleanup);

    const preset = (id: string) => {
        const found = PROVIDER_PRESETS.find(p => p.id === id);
        if (!found) throw new Error(`missing preset ${id}`);
        return found;
    };

    it('shows the directory form instead of the OpenID Connect one', () => {
        open(null, undefined, preset('ldap'));

        expect(screen.getByLabelText('Host')).toBeInTheDocument();
        expect(screen.getByLabelText('Bind DN')).toBeInTheDocument();
        expect(screen.getByLabelText('User search base')).toBeInTheDocument();
        expect(screen.getByLabelText('User filter')).toHaveValue('(&(objectClass=person)(|(uid={username})(mail={username})))');
        expect(screen.getByLabelText('Name')).toHaveValue('Company directory');
        expect(screen.queryByLabelText('Redirect address to register at the provider')).not.toBeInTheDocument();
        expect(screen.queryByLabelText('Issuer address')).not.toBeInTheDocument();
        expect(screen.queryByLabelText('Client secret')).not.toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Add LDAP directory' })).toBeInTheDocument();
    });

    it('saves a directory with its connection, service account and policies', async () => {
        const onSave = open(null, undefined, preset('ldap'));

        fireEvent.change(screen.getByLabelText('Host'), { target: { value: 'ldap.example.com' } });
        fireEvent.change(screen.getByLabelText('Port (optional)'), { target: { value: '6360x' } });
        fireEvent.change(screen.getByLabelText('Bind DN'), { target: { value: 'cn=vora,ou=service,dc=example,dc=com' } });
        fireEvent.change(screen.getByLabelText('Bind password'), { target: { value: 'svc-secret' } });
        fireEvent.change(screen.getByLabelText('User search base'), { target: { value: 'ou=people,dc=example,dc=com' } });
        fireEvent.click(screen.getByRole('switch', { name: 'Turned on' }));
        fireEvent.click(screen.getByRole('button', { name: 'Save provider' }));

        await waitFor(() => expect(onSave).toHaveBeenCalled());
        const request = onSave.mock.calls[0][0];
        expect(request).toMatchObject({
            type: 'Ldap', slug: 'ldap', displayName: 'Company directory', enabled: true, sortOrder: 5, ldapHost: 'ldap.example.com', ldapPort: 6360,
            ldapSecurity: 'Ldaps', ldapBindDn: 'cn=vora,ou=service,dc=example,dc=com', ldapBindPassword: 'svc-secret',
            ldapUserBaseDn: 'ou=people,dc=example,dc=com', newUserPolicy: 'FollowRegistrationMode', linkByEmail: 'ConfirmWithPassword', adminGroup: null,
        });
        expect('issuer' in request).toBe(false);
    });

    it('never shows a saved bind password and leaves it alone when the field stays empty', async () => {
        const onSave = open(savedDirectory);

        const password = screen.getByLabelText('Bind password');
        expect(password).toHaveValue('');
        expect(password).toHaveAttribute('type', 'password');
        expect(password).toHaveAttribute('placeholder', expect.stringMatching(/password is saved/));
        expect(screen.getByLabelText('Host')).toHaveValue('ldap.example.com');

        fireEvent.click(screen.getByRole('button', { name: 'Save provider' }));

        await waitFor(() => expect(onSave).toHaveBeenCalled());
        const request = onSave.mock.calls[0][0];
        expect('ldapBindPassword' in request).toBe(false);
        expect('clearLdapBindPassword' in request).toBe(false);
        expect(request.sortOrder).toBe(1);
    });

    it('removes the saved bind password when asked to', async () => {
        const onSave = open(savedDirectory);

        fireEvent.click(screen.getByLabelText('Remove the saved password'));
        fireEvent.click(screen.getByRole('button', { name: 'Save provider' }));

        await waitFor(() => expect(onSave).toHaveBeenCalled());
        expect(onSave.mock.calls[0][0].clearLdapBindPassword).toBe(true);
    });

    it('offers the certificate box only for an encrypted connection and makes plain ldap an explicit choice', async () => {
        const onSave = open(null, undefined, preset('ldap'));
        fireEvent.change(screen.getByLabelText('Host'), { target: { value: '192.168.1.20' } });
        expect(screen.getByLabelText('Certificate authority (optional)')).toBeInTheDocument();

        fireEvent.click(screen.getByRole('radio', { name: /No encryption/ }));

        expect(screen.queryByLabelText('Certificate authority (optional)')).not.toBeInTheDocument();
        expect(screen.getByRole('note')).toHaveTextContent(/can be read by anything on the path/);
        fireEvent.click(screen.getByRole('button', { name: 'Save provider' }));
        expect(await screen.findByRole('alert')).toHaveTextContent(/Tick the box/);
        expect(onSave).not.toHaveBeenCalled();

        fireEvent.click(screen.getByLabelText(/I understand, and this directory is on a network I trust/));
        fireEvent.click(screen.getByRole('button', { name: 'Save provider' }));

        await waitFor(() => expect(onSave).toHaveBeenCalled());
        expect(onSave.mock.calls[0][0].ldapSecurity).toBe('None');
    });

    it('does not save a filter that has no place for the typed name', async () => {
        const onSave = open(null, undefined, preset('ldap'));

        fireEvent.change(screen.getByLabelText('User filter'), { target: { value: '(uid=fixed)' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save provider' }));

        expect(await screen.findByRole('alert')).toHaveTextContent(/must contain \{username\}/);
        expect(onSave).not.toHaveBeenCalled();
    });

    it('sends a pasted certificate authority and a group search', async () => {
        const onSave = open(null, undefined, preset('ldap'));
        const pem = '-----BEGIN CERTIFICATE-----\nAAAA\n-----END CERTIFICATE-----';

        fireEvent.change(screen.getByLabelText('Certificate authority (optional)'), { target: { value: pem } });
        fireEvent.change(screen.getByLabelText('Group search base'), { target: { value: 'ou=groups,dc=example,dc=com' } });
        fireEvent.change(screen.getByLabelText('Group filter'), { target: { value: '(&(objectClass=groupOfNames)(member={dn}))' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save provider' }));

        await waitFor(() => expect(onSave).toHaveBeenCalled());
        expect(onSave.mock.calls[0][0]).toMatchObject({ ldapCaCertificatePem: pem, ldapGroupBaseDn: 'ou=groups,dc=example,dc=com', ldapGroupFilter: '(&(objectClass=groupOfNames)(member={dn}))' });
    });

    it('starts Active Directory from its field defaults', () => {
        open(null, undefined, preset('active-directory'));

        expect(screen.getByLabelText('Name')).toHaveValue('Active Directory');
        const filter = (screen.getByLabelText('User filter') as HTMLInputElement).value;
        expect(filter).toContain('sAMAccountName={username}');
        expect(filter).toContain('userAccountControl:1.2.840.113556.1.4.803:=2');
        fireEvent.click(screen.getByRole('button', { name: 'Show attribute names' }));
        expect(screen.getByLabelText('Unique id')).toHaveValue('objectGUID');
        expect(screen.getByLabelText('Groups')).toHaveValue('memberOf');
    });

    it('starts OpenLDAP, LLDAP and Authentik from the standard schema', () => {
        open(null, undefined, preset('openldap'));

        expect(screen.getByLabelText('User filter')).toHaveValue('(&(objectClass=inetOrgPerson)(|(uid={username})(mail={username})))');
        fireEvent.click(screen.getByRole('button', { name: 'Show attribute names' }));
        expect(screen.getByLabelText('Unique id')).toHaveValue('entryUUID');
        expect(screen.getByText(/LLDAP: unique id attribute uuid/)).toBeInTheDocument();
        expect(screen.getByText(/Authentik: search base ou=users below the provider's base DN, unique id attribute uid, filter on cn instead of uid/)).toBeInTheDocument();
        expect(screen.getByText(/FreeIPA: unique id attribute ipaUniqueID/)).toBeInTheDocument();
    });

    it('describes the policies in terms of the directory and treats directory email as verified', () => {
        open(null, undefined, preset('ldap'));

        fireEvent.click(screen.getByRole('radio', { name: /The identity provider decides/ }));

        expect(screen.getByRole('note')).toHaveTextContent(/anyone the directory accepts/);
        expect(screen.getByText(/The directory is the authority on its email addresses/)).toBeInTheDocument();
        expect(screen.getByText(/anyone who can set the mail attribute there could reach that account/)).toBeInTheDocument();
    });
});
