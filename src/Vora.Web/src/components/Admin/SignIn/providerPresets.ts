import type { ProviderDraft } from './providerDraft';

export interface ProviderPreset {
    id: 'google' | 'microsoft' | 'generic';
    label: string;
    description: string;
    steps: string;
    draft: Partial<ProviderDraft>;
}

export const PROVIDER_PRESETS: ProviderPreset[] = [
    {
        id: 'generic',
        label: 'OpenID Connect',
        description: 'Authentik, Keycloak, Authelia, Pocket ID, Zitadel or any OpenID Connect server.',
        steps: 'Create an OpenID Connect (OAuth) application there, add the redirect address shown below, then paste its issuer address, client id and secret here.',
        draft: { displayName: 'Single sign-on', slug: 'sso' },
    },
    {
        id: 'google',
        label: 'Google',
        description: 'Sign in with a Google account.',
        steps: 'In Google Cloud Console, open APIs & Services, then Credentials, and create an OAuth client ID of type Web application. Add the redirect address shown below as an authorized redirect URI, then paste the client id and secret here.',
        draft: { displayName: 'Google', slug: 'google', issuer: 'https://accounts.google.com' },
    },
    {
        id: 'microsoft',
        label: 'Microsoft',
        description: 'Sign in with a Microsoft Entra (work, school or personal) account.',
        steps: 'In the Microsoft Entra admin center, register an application, add a Web redirect URI with the address shown below, and create a client secret. Replace <tenant-id> in the issuer with your Directory (tenant) ID, then paste the application (client) id and secret here.',
        draft: { displayName: 'Microsoft', slug: 'microsoft', issuer: 'https://login.microsoftonline.com/<tenant-id>/v2.0' },
    },
];
