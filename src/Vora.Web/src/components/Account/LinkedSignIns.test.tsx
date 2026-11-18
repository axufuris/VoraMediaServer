import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import LinkedSignIns, { LAST_WAY_IN } from './LinkedSignIns';
import type { LinkedSignIn, SignInProviders } from '../../api/Auth/externalSignInService';
import { fakeDesktopBridge } from '../../test/fakeDesktopBridge';

const mocks = vi.hoisted(() => ({
    listLinked: vi.fn<() => Promise<LinkedSignIn[]>>(),
    getProvidersFor: vi.fn<() => Promise<SignInProviders>>(),
    unlink: vi.fn(),
    startLink: vi.fn(),
    confirm: vi.fn(),
    openExternal: vi.fn<(url: string) => Promise<boolean>>(),
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
        mocks.getProvidersFor.mockResolvedValue({ providers: [authentik, google], passwordSignInEnabled: true, directorySignInEnabled: false, registrationMode: 'Simple' });
        mocks.confirm.mockResolvedValue(true);
        Object.defineProperty(window, 'location', { configurable: true, value: { ...originalLocation, assign: vi.fn() } });
    });

    afterEach(() => {
        cleanup();
        Object.defineProperty(window, 'location', { configurable: true, value: originalLocation });
    });

    it('shows nothing on a server with no provider and no link', async () => {
        mocks.listLinked.mockResolvedValue([]);
        mocks.getProvidersFor.mockResolvedValue({ providers: [], passwordSignInEnabled: true, directorySignInEnabled: false, registrationMode: 'Simple' });
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
        mocks.getProvidersFor.mockResolvedValue({ providers: [authentik], passwordSignInEnabled: false, directorySignInEnabled: false, registrationMode: 'Simple' });
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

describe('LinkedSignIns inside Vora Desktop', () => {
    const originalLocation = window.location;
    const idpUrl = 'https://auth.example.com/authorize?x=1';

    const flush = (ms = 0) => act(async () => { await vi.advanceTimersByTimeAsync(ms); });

    const startLinkingGoogle = async () => {
        renderPanel(true);
        const button = await screen.findByRole('button', { name: 'Link Google' });
        vi.useFakeTimers();
        await act(async () => {
            fireEvent.click(button);
            await vi.advanceTimersByTimeAsync(0);
        });
    };

    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.listLinked.mockResolvedValue([]);
        mocks.getProvidersFor.mockResolvedValue({ providers: [authentik, google], passwordSignInEnabled: true, directorySignInEnabled: false, registrationMode: 'Simple' });
        mocks.startLink.mockResolvedValue(idpUrl);
        mocks.openExternal.mockResolvedValue(true);
        window.voraDesktop = fakeDesktopBridge({ openExternal: mocks.openExternal }).bridge;
        Object.defineProperty(window, 'location', { configurable: true, value: { ...originalLocation, assign: vi.fn() } });
    });

    afterEach(() => {
        vi.useRealTimers();
        cleanup();
        delete window.voraDesktop;
        Object.defineProperty(window, 'location', { configurable: true, value: originalLocation });
    });

    it("opens the provider's link address in the system browser and leaves the window where it is", async () => {
        await startLinkingGoogle();

        expect(mocks.startLink).toHaveBeenCalledWith('google', undefined);
        expect(mocks.openExternal).toHaveBeenCalledWith(idpUrl);
        expect(window.location.assign).not.toHaveBeenCalled();
    });

    it('says to finish in the browser, with a way to give up', async () => {
        await startLinkingGoogle();

        expect(screen.getByRole('status')).toHaveTextContent('Finish linking Google in your browser');
        expect(screen.getByRole('button', { name: 'Link Google' })).toBeEnabled();
        expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
    });

    it('shows the new link when the window gets focus back', async () => {
        await startLinkingGoogle();
        mocks.listLinked.mockResolvedValue([identity('i2', 'p2', 'Google')]);

        await act(async () => {
            window.dispatchEvent(new Event('focus'));
            await vi.advanceTimersByTimeAsync(0);
        });

        expect(screen.getByRole('status')).toHaveTextContent('Linked. You can now sign in with it.');
        expect(screen.getByText('Google')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Link Google' })).not.toBeInTheDocument();
        expect(screen.queryByText(/Finish linking/)).not.toBeInTheDocument();
    });

    it('also checks every few seconds while it waits', async () => {
        await startLinkingGoogle();
        const before = mocks.listLinked.mock.calls.length;
        mocks.listLinked.mockResolvedValue([identity('i2', 'p2', 'Google')]);

        await flush(3000);

        expect(mocks.listLinked.mock.calls.length).toBeGreaterThan(before);
        expect(screen.getByRole('status')).toHaveTextContent('Linked. You can now sign in with it.');
    });

    it('keeps waiting, and keeps the list, when a check fails', async () => {
        mocks.listLinked.mockResolvedValue([identity('i1', 'p1', 'Authentik')]);
        await startLinkingGoogle();
        mocks.listLinked.mockRejectedValue(new Error('offline'));

        await flush(3000);

        expect(screen.getByRole('status')).toHaveTextContent('Finish linking Google in your browser');
        expect(screen.getByText('Authentik')).toBeInTheDocument();
    });

    it('stops checking on a timer after a bounded number of tries', async () => {
        await startLinkingGoogle();

        await flush(60 * 3000);
        const settled = mocks.listLinked.mock.calls.length;
        await flush(10 * 3000);

        expect(mocks.listLinked.mock.calls.length).toBe(settled);
    });

    it('stops checking when the person gives up', async () => {
        await startLinkingGoogle();

        await act(async () => {
            fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
            await vi.advanceTimersByTimeAsync(0);
        });
        const settled = mocks.listLinked.mock.calls.length;
        await flush(9000);
        await act(async () => {
            window.dispatchEvent(new Event('focus'));
            await vi.advanceTimersByTimeAsync(0);
        });

        expect(screen.queryByText(/Finish linking/)).not.toBeInTheDocument();
        expect(mocks.listLinked.mock.calls.length).toBe(settled);
    });

    it('says so, and does not wait, when the browser could not be opened', async () => {
        mocks.openExternal.mockResolvedValue(false);

        await startLinkingGoogle();

        expect(screen.getByRole('alert')).toHaveTextContent('could not open your browser, so it cannot link Google');
        expect(screen.queryByText(/Finish linking/)).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Link Google' })).toBeEnabled();
        expect(window.location.assign).not.toHaveBeenCalled();
    });

    it('shows the failure and does not wait when linking cannot start', async () => {
        mocks.startLink.mockRejectedValue(new Error('The sign-in provider is not available.'));

        await startLinkingGoogle();

        expect(screen.getByRole('alert')).toHaveTextContent('The sign-in provider is not available.');
        expect(mocks.openExternal).not.toHaveBeenCalled();
        expect(screen.queryByText(/Finish linking/)).not.toBeInTheDocument();
    });

    it('keeps sending the window to the provider when this Desktop cannot open a browser', async () => {
        window.voraDesktop = fakeDesktopBridge({ openExternal: undefined }).bridge;

        renderPanel(true);
        fireEvent.click(await screen.findByRole('button', { name: 'Link Google' }));

        await waitFor(() => expect(window.location.assign).toHaveBeenCalledWith(idpUrl));
        expect(mocks.openExternal).not.toHaveBeenCalled();
        expect(screen.queryByText(/Finish linking/)).not.toBeInTheDocument();
    });
});
