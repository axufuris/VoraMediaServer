import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, AxiosHeaders } from 'axios';
import SignInSettingsCard from './SignInSettingsCard';
import type { AuthSettings } from '../../../api/Auth/signInProvidersAdminService';

const mocks = vi.hoisted(() => ({
    updateSettings: vi.fn(),
    confirm: vi.fn(),
}));

vi.mock('../../../api/Auth/signInProvidersAdminService', () => ({
    signInProvidersAdminService: { updateSettings: mocks.updateSettings },
}));
vi.mock('../../../dialogs', () => ({
    useDialog: () => ({ alert: vi.fn(), confirm: (options: unknown) => mocks.confirm(options), prompt: vi.fn() }),
}));

const settings = (overrides: Partial<AuthSettings> = {}): AuthSettings => ({
    passwordSignInEnabled: true,
    publicBaseUrl: null,
    effectiveBaseUrl: 'http://192.168.0.5:8080',
    ...overrides,
});

const problem = (detail: string) => new AxiosError('bad', '400', undefined, undefined, {
    status: 400, statusText: 'Bad Request', headers: {}, config: { headers: new AxiosHeaders() }, data: { title: 'Bad request', detail },
});

describe('SignInSettingsCard', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.confirm.mockResolvedValue(true);
    });

    afterEach(cleanup);

    it('says what providers are told today and saves a public address', async () => {
        const onChanged = vi.fn();
        mocks.updateSettings.mockResolvedValue(settings({ publicBaseUrl: 'https://vora.example.com', effectiveBaseUrl: 'https://vora.example.com' }));
        render(<SignInSettingsCard settings={settings()} enabledProviderCount={1} onChanged={onChanged} />);

        expect(screen.getByText('http://192.168.0.5:8080')).toBeInTheDocument();
        const save = screen.getByRole('button', { name: 'Save' });
        expect(save).toBeDisabled();

        fireEvent.change(screen.getByLabelText('Public address of this server'), { target: { value: ' https://vora.example.com ' } });
        fireEvent.click(save);

        await waitFor(() => expect(onChanged).toHaveBeenCalled());
        expect(mocks.updateSettings).toHaveBeenCalledWith({ passwordSignInEnabled: true, publicBaseUrl: 'https://vora.example.com' }, undefined);
    });

    it('is on by default and words the off state as requiring SSO for everyone except admins', () => {
        const { rerender } = render(<SignInSettingsCard settings={settings()} enabledProviderCount={1} onChanged={vi.fn()} />);

        expect(screen.getByRole('switch', { name: 'Allow password sign-in' })).toBeChecked();

        rerender(<SignInSettingsCard settings={settings({ passwordSignInEnabled: false })} enabledProviderCount={1} onChanged={vi.fn()} />);

        expect(screen.getByRole('switch', { name: 'Allow password sign-in' })).not.toBeChecked();
        expect(screen.getByText(/Require SSO for everyone except admins/)).toBeInTheDocument();
    });

    it('asks before turning password sign-in off, then turns it off', async () => {
        const onChanged = vi.fn();
        mocks.updateSettings.mockResolvedValue(settings({ passwordSignInEnabled: false }));
        render(<SignInSettingsCard settings={settings({ publicBaseUrl: 'https://vora.example.com' })} enabledProviderCount={1} onChanged={onChanged} />);

        fireEvent.click(screen.getByRole('switch', { name: 'Allow password sign-in' }));

        await waitFor(() => expect(mocks.updateSettings).toHaveBeenCalled());
        expect(mocks.confirm).toHaveBeenCalledWith(expect.objectContaining({ title: 'Require SSO for everyone except admins?' }));
        expect(mocks.updateSettings).toHaveBeenCalledWith({ passwordSignInEnabled: false, publicBaseUrl: 'https://vora.example.com' }, undefined);
        expect(onChanged).toHaveBeenCalledWith(expect.objectContaining({ passwordSignInEnabled: false }));
    });

    it('leaves password sign-in on when the question is declined', async () => {
        mocks.confirm.mockResolvedValue(false);
        render(<SignInSettingsCard settings={settings()} enabledProviderCount={1} onChanged={vi.fn()} />);

        fireEvent.click(screen.getByRole('switch', { name: 'Allow password sign-in' }));

        await waitFor(() => expect(mocks.confirm).toHaveBeenCalled());
        expect(mocks.updateSettings).not.toHaveBeenCalled();
    });

    it("shows the server's refusal when no provider is turned on", async () => {
        mocks.updateSettings.mockRejectedValue(problem('Turn on a sign-in provider first, so people have another way in.'));
        render(<SignInSettingsCard settings={settings()} enabledProviderCount={0} onChanged={vi.fn()} />);

        fireEvent.click(screen.getByRole('switch', { name: 'Allow password sign-in' }));

        expect(await screen.findByRole('alert')).toHaveTextContent('Turn on a sign-in provider first');
        expect(mocks.confirm).not.toHaveBeenCalled();
    });

    it('turns password sign-in back on without asking', async () => {
        mocks.updateSettings.mockResolvedValue(settings());
        render(<SignInSettingsCard settings={settings({ passwordSignInEnabled: false })} enabledProviderCount={1} onChanged={vi.fn()} />);

        fireEvent.click(screen.getByRole('switch', { name: 'Allow password sign-in' }));

        await waitFor(() => expect(mocks.updateSettings).toHaveBeenCalledWith({ passwordSignInEnabled: true, publicBaseUrl: null }, undefined));
        expect(mocks.confirm).not.toHaveBeenCalled();
    });
});

describe('SignInSettingsCard with a directory', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.confirm.mockResolvedValue(true);
    });

    afterEach(cleanup);

    it('says directory sign-in keeps working when passwords kept here are turned off', () => {
        render(<SignInSettingsCard settings={settings({ passwordSignInEnabled: false })} enabledProviderCount={1} directoryEnabled onChanged={vi.fn()} />);

        expect(screen.getByText(/Directory \(LDAP\) passwords stay in the directory, so directory sign-in keeps working/)).toBeInTheDocument();
    });

    it('does not mention a directory when none is on', () => {
        render(<SignInSettingsCard settings={settings({ passwordSignInEnabled: false })} enabledProviderCount={1} onChanged={vi.fn()} />);

        expect(screen.queryByText(/Directory \(LDAP\)/)).not.toBeInTheDocument();
    });

    it('names the directory account in the question before turning password sign-in off', async () => {
        mocks.updateSettings.mockResolvedValue(settings({ passwordSignInEnabled: false }));
        render(<SignInSettingsCard settings={settings()} enabledProviderCount={1} directoryEnabled onChanged={vi.fn()} />);

        fireEvent.click(screen.getByRole('switch', { name: 'Allow password sign-in' }));

        await waitFor(() => expect(mocks.confirm).toHaveBeenCalled());
        expect(mocks.confirm.mock.calls[0][0]).toMatchObject({ message: expect.stringContaining('their directory account or a sign-in provider') });
        await waitFor(() => expect(mocks.updateSettings).toHaveBeenCalledWith({ passwordSignInEnabled: false, publicBaseUrl: null }, undefined));
    });
});
