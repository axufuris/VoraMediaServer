import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import ExternalSignInPage from './ExternalSignInPage';
import type { ExternalComplete, ExternalPending } from '../../api/Auth/externalSignInService';

const mocks = vi.hoisted(() => ({
    probeServer: vi.fn(),
    exchange: vi.fn(),
    getPending: vi.fn<() => Promise<ExternalPending>>(),
    complete: vi.fn<(baseUrl: string, request: unknown) => Promise<ExternalComplete>>(),
}));

vi.mock('../../api/Auth/authService', () => ({ authService: { probeServer: mocks.probeServer } }));
vi.mock('../../api/Auth/externalSignInService', () => ({
    externalSignInService: { exchange: mocks.exchange, getPending: mocks.getPending, complete: mocks.complete },
}));

const pending = (overrides: Partial<ExternalPending> = {}): ExternalPending => ({
    outcome: 'NeedsCreateOrLink',
    refusal: 'None',
    message: null,
    canCreateAccount: true,
    passwordRejected: false,
    invitePinRejected: false,
    providerName: 'Authentik',
    email: 'sam@example.com',
    ...overrides,
});

const completed = (overrides: Partial<ExternalComplete> = {}): ExternalComplete => ({
    outcome: 'SignedIn',
    refusal: 'None',
    message: null,
    code: 'one-time',
    returnTo: null,
    isNewAccount: false,
    canCreateAccount: false,
    passwordRejected: false,
    invitePinRejected: false,
    ...overrides,
});

const renderAt = (search: string) => render(
    <MemoryRouter initialEntries={[`/login/external${search}`]}>
        <Routes>
            <Route path="/login/external" element={<ExternalSignInPage />} />
            <Route path="/profiles" element={<div>profile picker</div>} />
            <Route path="/login" element={<div>login page</div>} />
        </Routes>
    </MemoryRouter>,
);

describe('ExternalSignInPage', () => {
    beforeEach(() => {
        localStorage.clear();
        sessionStorage.clear();
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.probeServer.mockResolvedValue({ isClaimed: true, registrationMode: 1, serverName: 'Home' });
        mocks.exchange.mockResolvedValue({ accessToken: 'account-jwt', userId: 'u1', displayName: 'Sam', isAdmin: false });
    });

    afterEach(cleanup);

    it('trades the one-time code for the account token and goes to the profile picker', async () => {
        renderAt('?code=abc123');

        expect(await screen.findByText('profile picker')).toBeInTheDocument();
        expect(mocks.exchange).toHaveBeenCalledTimes(1);
        expect(mocks.exchange.mock.calls[0][1]).toBe('abc123');
        expect(localStorage.getItem('account_token')).toBe('account-jwt');
        expect(localStorage.getItem('user_id')).toBe('u1');
        expect(sessionStorage.getItem('pending_user_token')).toBe('account-jwt');
    });

    it('says so when the code was used or has expired', async () => {
        mocks.exchange.mockRejectedValue(new Error('401'));
        renderAt('?code=old');

        expect(await screen.findByRole('alert')).toHaveTextContent(/expired or was already used/);
        expect(localStorage.getItem('account_token')).toBeNull();
        expect(screen.getByRole('link', { name: 'Back to Sign In' })).toBeInTheDocument();
    });

    it.each([
        ['access_denied', /cancelled/],
        ['state_expired', /took too long/],
        ['token_exchange_failed', /client id and secret/],
        ['something_new', /didn't finish/],
    ])('explains the %s error in plain words', async (code, expected) => {
        renderAt(`?error=${code}`);

        expect(await screen.findByRole('alert')).toHaveTextContent(expected);
        expect(mocks.exchange).not.toHaveBeenCalled();
    });

    it('asks for the local account password once when the email matches an account', async () => {
        mocks.getPending.mockResolvedValue(pending({ outcome: 'NeedsLinkConfirmation' }));
        mocks.complete
            .mockResolvedValueOnce(completed({ outcome: 'NeedsLinkConfirmation', code: null, passwordRejected: true }))
            .mockResolvedValueOnce(completed());
        renderAt('?pending=handle-1');

        expect(await screen.findByText(/already exists on this server/)).toBeInTheDocument();
        expect(screen.getByLabelText('Email of your account here')).toHaveValue('sam@example.com');

        fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'wrong' } });
        fireEvent.click(screen.getByRole('button', { name: 'Link Authentik' }));
        expect(await screen.findByRole('alert')).toHaveTextContent(/don't match/);
        expect(mocks.complete).toHaveBeenLastCalledWith(expect.any(String), { handle: 'handle-1', linkEmail: 'sam@example.com', linkPassword: 'wrong' });

        fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'right-password' } });
        fireEvent.click(screen.getByRole('button', { name: 'Link Authentik' }));

        expect(await screen.findByText('profile picker')).toBeInTheDocument();
        expect(mocks.exchange.mock.calls[0][1]).toBe('one-time');
    });

    it('asks for the invite PIN and tells the person when it is wrong', async () => {
        mocks.getPending.mockResolvedValue(pending({ outcome: 'NeedsInvitePin' }));
        mocks.complete.mockResolvedValueOnce(completed({ outcome: 'NeedsInvitePin', code: null, invitePinRejected: true }));
        renderAt('?pending=handle-2');

        const pin = await screen.findByLabelText('Invite PIN');
        fireEvent.change(pin, { target: { value: '12ab34' } });
        expect(pin).toHaveValue('1234');
        fireEvent.click(screen.getByRole('button', { name: 'Create my account' }));

        expect(await screen.findByRole('alert')).toHaveTextContent(/PIN isn't right/);
        expect(mocks.complete).toHaveBeenCalledWith(expect.any(String), { handle: 'handle-2', invitePin: '1234' });
    });

    it('lets a first-timer create an account or link the one they have', async () => {
        mocks.getPending.mockResolvedValue(pending());
        mocks.complete.mockResolvedValue(completed({ isNewAccount: true }));
        renderAt('?pending=handle-3');

        expect(await screen.findByRole('button', { name: 'I already have an account' })).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Create a new account' }));

        expect(await screen.findByText('profile picker')).toBeInTheDocument();
        expect(mocks.complete).toHaveBeenCalledWith(expect.any(String), { handle: 'handle-3', createAccount: true });
    });

    it('moves to the link form when the person already has an account', async () => {
        mocks.getPending.mockResolvedValue(pending());
        renderAt('?pending=handle-4');

        fireEvent.click(await screen.findByRole('button', { name: 'I already have an account' }));

        expect(await screen.findByLabelText('Email of your account here')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Back' })).toBeInTheDocument();
    });

    it("offers only linking when the server won't make accounts from this provider", async () => {
        mocks.getPending.mockResolvedValue(pending({ canCreateAccount: false }));
        renderAt('?pending=handle-5');

        expect(await screen.findByText(/doesn't create new accounts from Authentik/)).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Create a new account' })).not.toBeInTheDocument();
        expect(screen.getByLabelText('Email of your account here')).toBeInTheDocument();
    });

    it("shows the server's reason when the provider's sign-in is refused", async () => {
        mocks.getPending.mockResolvedValue(pending({ outcome: 'Refused', refusal: 'RegistrationClosed', canCreateAccount: false }));
        renderAt('?pending=handle-6');

        expect(await screen.findByRole('alert')).toHaveTextContent(/not creating new accounts/);
    });

    it('says the step expired when the pending handle is gone', async () => {
        mocks.getPending.mockRejectedValue(new Error('404'));
        renderAt('?pending=gone');

        await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(/step expired/));
    });
});
