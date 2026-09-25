import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { AxiosError, AxiosHeaders } from 'axios';
import AddUserModal from './AddUserModal';
import SignUpCard from './SignUpCard';

const mocks = vi.hoisted(() => ({
    createUser: vi.fn(),
    getServerSettings: vi.fn(),
    updateServerSettings: vi.fn(),
    generateInviteCode: vi.fn(),
    createInvitation: vi.fn(),
}));

vi.mock('../../../api/Users/userService', () => ({ userService: { createUser: mocks.createUser } }));
vi.mock('../../../api/System/systemSettingsAdminService', () => ({
    systemSettingsAdminService: { getServerSettings: mocks.getServerSettings, updateServerSettings: mocks.updateServerSettings },
}));
vi.mock('../../../api/Auth/authService', () => ({ authService: { generateInviteCode: mocks.generateInviteCode } }));
vi.mock('../../../api/Auth/invitationsAdminService', () => ({ invitationsAdminService: { create: mocks.createInvitation } }));

const withMode = (registrationMode: number) => mocks.getServerSettings.mockResolvedValue({ registrationMode, nightlyScanTime: '03:00:00' });

const renderCard = () => render(<MemoryRouter><SignUpCard invitationsPath="/admin/invitations" /></MemoryRouter>);

describe('AddUserModal', () => {
    beforeEach(() => Object.values(mocks).forEach(m => m.mockReset()));

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

    it('starts with a generated password long enough to be accepted', () => {
        render(<AddUserModal onClose={vi.fn()} onCreated={vi.fn()} />);

        expect((screen.getByLabelText('Starting password') as HTMLInputElement).value.length).toBeGreaterThanOrEqual(8);
    });
});

describe('SignUpCard', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.updateServerSettings.mockResolvedValue(undefined);
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

    it('saves a new mode straight away, keeping the rest of the settings', async () => {
        withMode(1);
        renderCard();

        fireEvent.change(await screen.findByRole('combobox', { name: 'How people sign up' }), { target: { value: '3' } });

        await waitFor(() => expect(mocks.updateServerSettings).toHaveBeenCalledWith(
            { registrationMode: 3, nightlyScanTime: '03:00:00' }, undefined));
        expect(await screen.findByRole('button', { name: 'Send invitation' })).toBeInTheDocument();
    });
});
