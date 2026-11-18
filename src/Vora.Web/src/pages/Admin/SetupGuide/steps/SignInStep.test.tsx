import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { SignInStep } from './AccessSteps';
import type { SetupStepProps } from '../setupContext';
import { noDirectoryFields } from '../../../../test/signInFixtures';
import type { AuthProviderAdmin } from '../../../../api/Auth/signInProvidersAdminService';

const mocks = vi.hoisted(() => ({ list: vi.fn<() => Promise<AuthProviderAdmin[]>>() }));

vi.mock('../../../../api/Auth/signInProvidersAdminService', () => ({ signInProvidersAdminService: { list: mocks.list } }));

const provider = (displayName: string, enabled: boolean): AuthProviderAdmin => ({
    ...noDirectoryFields,
    id: displayName, type: 'Oidc', slug: displayName.toLowerCase(), displayName, enabled, iconUrl: null, sortOrder: 0, issuer: 'https://x.example', clientId: 'c',
    hasSecret: true, scopes: 'openid', emailClaim: 'email', nameClaim: 'name', groupsClaim: 'groups', newUserPolicy: 'FollowRegistrationMode',
    linkByEmail: 'ConfirmWithPassword', allowedEmailDomains: [], requiredGroup: null, adminGroup: null, redirectUri: 'https://vora.example/cb',
});

const props = (serverId?: string) => ({ serverId } as SetupStepProps);

describe('SignInStep', () => {
    beforeEach(() => mocks.list.mockReset());
    afterEach(cleanup);

    it('says none is set up yet and links to the providers page in a new tab', async () => {
        mocks.list.mockResolvedValue([]);
        render(<SignInStep {...props()} />);

        expect(await screen.findByText('None set up yet')).toBeInTheDocument();
        const link = screen.getByRole('link', { name: 'Set up a provider' });
        expect(link).toHaveAttribute('href', '/admin/sign-in-providers');
        expect(link).toHaveAttribute('target', '_blank');
    });

    it('names the providers that are turned on, and scopes the link to the server', async () => {
        mocks.list.mockResolvedValue([provider('Authentik', true), provider('Google', false)]);
        render(<SignInStep {...props('s1')} />);

        expect(await screen.findByText('Turned on: Authentik')).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Set up a provider' })).toHaveAttribute('href', '/admin/server/s1/sign-in-providers');
    });
});
