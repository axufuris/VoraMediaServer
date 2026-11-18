import { describe, it, expect, vi, afterEach } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import ProviderEditorModal from './ProviderEditorModal';
import { PROVIDER_PRESETS } from './providerPresets';
import type { AuthProviderAdmin, SaveAuthProvider } from '../../../api/Auth/signInProvidersAdminService';

const saved: AuthProviderAdmin = {
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
