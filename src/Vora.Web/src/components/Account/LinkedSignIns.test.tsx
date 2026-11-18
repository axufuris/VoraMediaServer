import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import LinkedSignIns, { LAST_WAY_IN } from './LinkedSignIns';
import type { LinkedSignIn, SignInProviders } from '../../api/Auth/externalSignInService';

const mocks = vi.hoisted(() => ({
    listLinked: vi.fn<() => Promise<LinkedSignIn[]>>(),
    getProvidersFor: vi.fn<() => Promise<SignInProviders>>(),
    unlink: vi.fn(),
    startLink: vi.fn(),
    confirm: vi.fn(),
}));

vi.mock('../../api/Auth/externalSignInService', () => ({
    externalSignInService: {
        listLinked: mocks.listLinked,
        getProvidersFor: mocks.getProvidersFor,
        unlink: mocks.unlink,
        startLink: mocks.startLink,
    },
}));
vi.mock('../../dialogs', () => ({
    useDialog: () => ({ alert: vi.fn(), confirm: (options: unknown) => mocks.confirm(options), prompt: vi.fn() }),
}));

const authentik = { id: 'p1', slug: 'authentik', displayName: 'Authentik', type: 'Oidc' as const, iconUrl: null };
const google = { id: 'p2', slug: 'google', displayName: 'Google', type: 'Oidc' as const, iconUrl: null };

const identity = (id: string, providerId: string, providerName: string): LinkedSignIn => ({
    id, providerId, providerName, email: 'sam@example.com', linkedAt: '2026-11-01T00:00:00Z', lastSignInAt: null,
});

const renderPanel = (hasPassword: boolean, search = '') => render(
    <MemoryRouter initialEntries={[`/account${search}`]}>
        <LinkedSignIns hasPassword={hasPassword} />
    </MemoryRouter>,
);

describe('LinkedSignIns', () => {
    const originalLocation = window.location;

    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.getProvidersFor.mockResolvedValue({ providers: [authentik, google], passwordSignInEnabled: true, registrationMode: 'Simple' });
        mocks.confirm.mockResolvedValue(true);
        Object.defineProperty(window, 'location', { configurable: true, value: { ...originalLocation, assign: vi.fn() } });
    });

    afterEach(() => {
        cleanup();
        Object.defineProperty(window, 'location', { configurable: true, value: originalLocation });
    });

    it('shows nothing on a server with no provider and no link', async () => {
        mocks.listLinked.mockResolvedValue([]);
        mocks.getProvidersFor.mockResolvedValue({ providers: [], passwordSignInEnabled: true, registrationMode: 'Simple' });
        const { container } = renderPanel(true);

        await waitFor(() => expect(mocks.listLinked).toHaveBeenCalled());
        await waitFor(() => expect(container).toBeEmptyDOMElement());
    });

    it('lists what is linked and offers the providers that are not', async () => {
        mocks.listLinked.mockResolvedValue([identity('i1', 'p1', 'Authentik')]);
        renderPanel(true);

        expect(await screen.findByText('Authentik')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Unlink' })).toBeEnabled();
        expect(screen.getByRole('button', { name: 'Link Google' })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Link Authentik' })).not.toBeInTheDocument();
    });

    it('will not unlink the only way in: no password and one provider', async () => {
        mocks.listLinked.mockResolvedValue([identity('i1', 'p1', 'Authentik')]);
        renderPanel(false);

        const unlink = await screen.findByRole('button', { name: 'Unlink' });
        expect(unlink).toBeDisabled();
        expect(screen.getByText(LAST_WAY_IN)).toBeInTheDocument();
    });

    it('will unlink one of two providers even without a password', async () => {
        mocks.listLinked.mockResolvedValue([identity('i1', 'p1', 'Authentik'), identity('i2', 'p2', 'Google')]);
        renderPanel(false);

        const buttons = await screen.findAllByRole('button', { name: 'Unlink' });
        expect(buttons).toHaveLength(2);
        buttons.forEach(button => expect(button).toBeEnabled());
    });

    it('counts a password that cannot be used while password sign-in is off as no way in', async () => {
        mocks.listLinked.mockResolvedValue([identity('i1', 'p1', 'Authentik')]);
        mocks.getProvidersFor.mockResolvedValue({ providers: [authentik], passwordSignInEnabled: false, registrationMode: 'Simple' });
        renderPanel(true);

        expect(await screen.findByRole('button', { name: 'Unlink' })).toBeDisabled();
    });

    it('asks first, then unlinks and refreshes', async () => {
        mocks.listLinked.mockResolvedValueOnce([identity('i1', 'p1', 'Authentik')]).mockResolvedValue([]);
        mocks.unlink.mockResolvedValue(undefined);
        renderPanel(true);

        fireEvent.click(await screen.findByRole('button', { name: 'Unlink' }));

        await waitFor(() => expect(mocks.unlink).toHaveBeenCalledWith('i1', undefined));
        expect(mocks.confirm).toHaveBeenCalled();
        expect(await screen.findByRole('status')).toHaveTextContent('Authentik is no longer linked.');
    });

    it('does nothing when the confirmation is declined', async () => {
        mocks.listLinked.mockResolvedValue([identity('i1', 'p1', 'Authentik')]);
        mocks.confirm.mockResolvedValue(false);
        renderPanel(true);

        fireEvent.click(await screen.findByRole('button', { name: 'Unlink' }));

        await waitFor(() => expect(mocks.confirm).toHaveBeenCalled());
        expect(mocks.unlink).not.toHaveBeenCalled();
    });

    it("shows the server's refusal when it says that was the last way in", async () => {
        const { AxiosError, AxiosHeaders } = await import('axios');
        mocks.listLinked.mockResolvedValue([identity('i1', 'p1', 'Authentik')]);
        mocks.unlink.mockRejectedValue(new AxiosError('conflict', '409', undefined, undefined, {
            status: 409, statusText: 'Conflict', headers: {}, config: { headers: new AxiosHeaders() },
            data: { title: 'Conflict', detail: 'That is the only way you can sign in.' },
        }));
        renderPanel(true);

        fireEvent.click(await screen.findByRole('button', { name: 'Unlink' }));

        expect(await screen.findByRole('alert')).toHaveTextContent('That is the only way you can sign in.');
    });

    it("sends the browser to the provider's link address", async () => {
        mocks.listLinked.mockResolvedValue([]);
        mocks.startLink.mockResolvedValue('https://auth.example.com/authorize?x=1');
        renderPanel(true);

        fireEvent.click(await screen.findByRole('button', { name: 'Link Google' }));

        await waitFor(() => expect(mocks.startLink).toHaveBeenCalledWith('google', undefined));
        expect(window.location.assign).toHaveBeenCalledWith('https://auth.example.com/authorize?x=1');
    });

    it('confirms a link that just finished and a link that could not', async () => {
        mocks.listLinked.mockResolvedValue([identity('i1', 'p1', 'Authentik')]);
        renderPanel(true, '?linked=authentik');
        expect(await screen.findByRole('status')).toHaveTextContent(/Linked/);
        cleanup();

        mocks.listLinked.mockResolvedValue([]);
        renderPanel(true, '?linkError=link_conflict');
        expect(await screen.findByRole('alert')).toHaveTextContent(/already linked to a different account/);
    });
});
