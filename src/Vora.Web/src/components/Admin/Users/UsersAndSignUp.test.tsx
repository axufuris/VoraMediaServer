import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { AxiosError, AxiosHeaders } from 'axios';
import AddUserModal from './AddUserModal';
import SignUpCard from './SignUpCard';

const mocks = vi.hoisted(() => ({
    createUser: vi.fn(),
    createSsoOnlyUser: vi.fn(),
    getProvidersFor: vi.fn(),
    getServerSettings: vi.fn(),
    updateRegistrationMode: vi.fn(),
    generateInviteCode: vi.fn(),
    createInvitation: vi.fn(),
}));

vi.mock('../../../api/Users/userService', () => ({ userService: { createUser: mocks.createUser, createSsoOnlyUser: mocks.createSsoOnlyUser } }));
vi.mock('../../../api/Auth/externalSignInService', () => ({ externalSignInService: { getProvidersFor: mocks.getProvidersFor } }));
vi.mock('../../../api/System/systemSettingsAdminService', () => ({
    systemSettingsAdminService: { getServerSettings: mocks.getServerSettings, updateRegistrationMode: mocks.updateRegistrationMode },
}));
vi.mock('../../../api/Auth/authService', () => ({ authService: { generateInviteCode: mocks.generateInviteCode } }));
vi.mock('../../../api/Auth/invitationsAdminService', () => ({ invitationsAdminService: { create: mocks.createInvitation } }));

const withMode = (registrationMode: number) => mocks.getServerSettings.mockResolvedValue({ registrationMode, nightlyScanTime: '03:00:00' });

const renderCard = () => render(<MemoryRouter><SignUpCard invitationsPath="/admin/invitations" /></MemoryRouter>);

const withProviders = (passwordSignInEnabled = true) => mocks.getProvidersFor.mockResolvedValue({
    providers: [{ id: 'p1', slug: 'authentik', displayName: 'Authentik', type: 'Oidc', iconUrl: null }],
    passwordSignInEnabled,
    directorySignInEnabled: false,
    registrationMode: 'Simple',
});

describe('AddUserModal', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.getProvidersFor.mockResolvedValue({ providers: [], passwordSignInEnabled: true, directorySignInEnabled: false, registrationMode: 'Simple' });
    });

    it('creates the account and shows the details to pass on', async () => {
        mocks.createUser.mockResolvedValue({ id: 'u1' });
        const onCreated = vi.fn();
        render(<AddUserModal onClose={vi.fn()} onCreated={onCreated} />);

        fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Sam' } });
        fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'Sam@Example.com' } });
        fireEvent.change(screen.getByLabelText('Starting password'), { target: { value: 'a-long-password' } });
        fireEvent.click(screen.getByRole('button', { name: 'Create account' }));

        expect(await screen.findByText('Account created')).toBeInTheDocument();
        expect(mocks.createUser).toHaveBeenCalledWith('Sam@Example.com', 'Sam', 'a-long-password', undefined);
        expect(screen.getByText('sam@example.com')).toBeInTheDocument();
        expect(screen.getByText('a-long-password')).toBeInTheDocument();
        expect(onCreated).toHaveBeenCalled();
    });

    it("shows the server's reason when the email is taken", async () => {
        mocks.createUser.mockRejectedValue(new AxiosError('conflict', '409', undefined, undefined, {
            status: 409, statusText: 'Conflict', headers: {}, config: { headers: new AxiosHeaders() },
            data: { title: 'Conflict', detail: 'An account with that email already exists.' },
        }));
        render(<AddUserModal onClose={vi.fn()} onCreated={vi.fn()} />);

        fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Sam' } });
        fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'sam@example.com' } });
        fireEvent.click(screen.getByRole('button', { name: 'Create account' }));

        expect(await screen.findByRole('alert')).toHaveTextContent('An account with that email already exists.');
    });

    it('offers no sign-in choice on a server without providers', async () => {
        render(<AddUserModal onClose={vi.fn()} onCreated={vi.fn()} />);

        await waitFor(() => expect(mocks.getProvidersFor).toHaveBeenCalled());
        expect(screen.queryByRole('radiogroup', { name: 'How they sign in' })).not.toBeInTheDocument();
        expect(screen.getByLabelText('Starting password')).toBeInTheDocument();
    });

    it('adds an account with no password that signs in through a provider', async () => {
        withProviders();
        mocks.createSsoOnlyUser.mockResolvedValue({ id: 'u2' });
        const onCreated = vi.fn();
        render(<AddUserModal onClose={vi.fn()} onCreated={onCreated} />);

        fireEvent.click(await screen.findByRole('radio', { name: /Sign in with a provider/ }));
        expect(screen.queryByLabelText('Starting password')).not.toBeInTheDocument();
        fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Sam' } });
        fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'Sam@Example.com' } });
        fireEvent.click(screen.getByRole('button', { name: 'Create account' }));

        expect(await screen.findByText('Account created')).toBeInTheDocument();
        expect(mocks.createSsoOnlyUser).toHaveBeenCalledWith('Sam@Example.com', 'Sam', undefined);
        expect(mocks.createUser).not.toHaveBeenCalled();
        expect(screen.getByText(/sign in with Authentik using this email/)).toBeInTheDocument();
        expect(screen.queryByText('Password')).not.toBeInTheDocument();
        expect(onCreated).toHaveBeenCalled();
    });

    it('still adds an account with a password when a provider exists and that is chosen', async () => {
        withProviders();
        mocks.createUser.mockResolvedValue({ id: 'u3' });
        render(<AddUserModal onClose={vi.fn()} onCreated={vi.fn()} />);

        await screen.findByRole('radiogroup', { name: 'How they sign in' });
        fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Sam' } });
        fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'sam@example.com' } });
        fireEvent.change(screen.getByLabelText('Starting password'), { target: { value: 'a-long-password' } });
        fireEvent.click(screen.getByRole('button', { name: 'Create account' }));

        await waitFor(() => expect(mocks.createUser).toHaveBeenCalledWith('sam@example.com', 'Sam', 'a-long-password', undefined));
        expect(mocks.createSsoOnlyUser).not.toHaveBeenCalled();
    });

    it('makes the account provider-only when password sign-in is off', async () => {
        withProviders(false);
        mocks.createSsoOnlyUser.mockResolvedValue({ id: 'u4' });
        render(<AddUserModal onClose={vi.fn()} onCreated={vi.fn()} />);

        expect(await screen.findByText(/Password sign-in is off, so this account signs in with Authentik/)).toBeInTheDocument();
        expect(screen.queryByLabelText('Starting password')).not.toBeInTheDocument();
        fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Sam' } });
        fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'sam@example.com' } });
        fireEvent.click(screen.getByRole('button', { name: 'Create account' }));

        await waitFor(() => expect(mocks.createSsoOnlyUser).toHaveBeenCalled());
    });

    it('starts with a generated password long enough to be accepted', () => {
        render(<AddUserModal onClose={vi.fn()} onCreated={vi.fn()} />);

        expect((screen.getByLabelText('Starting password') as HTMLInputElement).value.length).toBeGreaterThanOrEqual(8);
    });
});

describe('SignUpCard', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.updateRegistrationMode.mockResolvedValue(undefined);
    });

    // Disabled is 0. The settings page used `mode || 1`, so it always showed Open.
    it('shows Disabled as Disabled, and says only admins add people', async () => {
        withMode(0);
        renderCard();

        expect(await screen.findByText(/Nobody can create an account themselves/)).toBeInTheDocument();
        expect(screen.getByRole('combobox', { name: 'How people sign up' })).toHaveValue('0');
    });

    it('offers the sign-up link when sign-up is open', async () => {
        withMode(1);
        renderCard();

        expect(await screen.findByRole('button', { name: 'Copy link' })).toBeInTheDocument();
        expect(screen.getByText(`${window.location.origin}/register`)).toBeInTheDocument();
    });

    it('generates a PIN in PIN mode', async () => {
        withMode(2);
        mocks.generateInviteCode.mockResolvedValue('4821');
        renderCard();

        fireEvent.click(await screen.findByRole('button', { name: 'Generate PIN' }));

        expect(await screen.findByText('4821')).toBeInTheDocument();
    });

    it('sends an email invitation in invitation-only mode', async () => {
        withMode(3);
        mocks.createInvitation.mockResolvedValue({ invitation: { id: 'i' }, emailSent: true, message: null });
        renderCard();

        fireEvent.change(await screen.findByLabelText('Email address to invite'), { target: { value: 'sam@example.com' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send invitation' }));

        expect(await screen.findByRole('status')).toHaveTextContent('Invitation sent to sam@example.com.');
        expect(mocks.createInvitation).toHaveBeenCalledWith('sam@example.com', null, undefined);
    });

    it('saves a new mode straight away, and only the mode', async () => {
        withMode(1);
        renderCard();
        await screen.findByRole('button', { name: 'Copy link' });

        fireEvent.change(screen.getByRole('combobox', { name: 'How people sign up' }), { target: { value: '3' } });

        await waitFor(() => expect(mocks.updateRegistrationMode).toHaveBeenCalledWith(3, undefined));
        expect(await screen.findByRole('button', { name: 'Send invitation' })).toBeInTheDocument();
    });
});

describe('AddUserModal with a directory', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
    });

    it('offers an account that signs in through the directory and says so on the confirmation', async () => {
        mocks.getProvidersFor.mockResolvedValue({ providers: [], passwordSignInEnabled: true, directorySignInEnabled: true, registrationMode: 'Simple' });
        mocks.createSsoOnlyUser.mockResolvedValue({ id: 'u3' });
        render(<AddUserModal onClose={vi.fn()} onCreated={vi.fn()} />);

        fireEvent.click(await screen.findByRole('radio', { name: /Sign in with a provider/ }));
        fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Sam' } });
        fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'sam@example.com' } });
        fireEvent.click(screen.getByRole('button', { name: 'Create account' }));

        expect(await screen.findByText('Account created')).toBeInTheDocument();
        expect(mocks.createSsoOnlyUser).toHaveBeenCalledWith('sam@example.com', 'Sam', undefined);
        expect(screen.getByText(/sign in with their directory account using this email/)).toBeInTheDocument();
    });

    it('makes the account passwordless when passwords kept here are off and only a directory is on', async () => {
        mocks.getProvidersFor.mockResolvedValue({ providers: [], passwordSignInEnabled: false, directorySignInEnabled: true, registrationMode: 'Simple' });
        render(<AddUserModal onClose={vi.fn()} onCreated={vi.fn()} />);

        expect(await screen.findByText(/Password sign-in is off, so this account signs in with their directory account/)).toBeInTheDocument();
        expect(screen.queryByLabelText('Starting password')).not.toBeInTheDocument();
    });
});
