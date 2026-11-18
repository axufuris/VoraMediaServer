import { apiClient, createDirectClient } from '../client';
import type { AuthResponse } from './authService';

export type AuthProviderType = 'Oidc' | 'Ldap';
export type RegistrationModeName = 'Disabled' | 'Simple' | 'SecretWord' | 'Invitation';

export interface SignInProvider {
    id: string;
    slug: string;
    displayName: string;
    type: AuthProviderType;
    iconUrl: string | null;
}

export interface SignInProviders {
    providers: SignInProvider[];
    passwordSignInEnabled: boolean;
    directorySignInEnabled: boolean;
    registrationMode: RegistrationModeName;
}

export type ExternalOutcome = 'SignedIn' | 'NeedsLinkConfirmation' | 'NeedsInvitePin' | 'NeedsCreateOrLink' | 'Refused';

export type ExternalRefusal =
    | 'None' | 'ProviderUnavailable' | 'EmailMissing' | 'EmailNotVerified' | 'EmailDomainNotAllowed' | 'GroupNotAllowed'
    | 'RegistrationClosed' | 'InvitationRequired' | 'InvitationMismatch' | 'EmailInUse' | 'AlreadyLinked';

export interface ExternalPending {
    outcome: ExternalOutcome;
    refusal: ExternalRefusal;
    message: string | null;
    canCreateAccount: boolean;
    passwordRejected: boolean;
    invitePinRejected: boolean;
    providerName: string;
    email: string | null;
}

export interface ExternalComplete {
    outcome: ExternalOutcome;
    refusal: ExternalRefusal;
    message: string | null;
    code: string | null;
    returnTo: string | null;
    isNewAccount: boolean;
    canCreateAccount: boolean;
    passwordRejected: boolean;
    invitePinRejected: boolean;
}

export interface ExternalCompleteRequest {
    handle: string;
    createAccount?: boolean;
    invitePin?: string;
    linkEmail?: string;
    linkPassword?: string;
}

export interface LinkedSignIn {
    id: string;
    providerId: string;
    providerName: string;
    email: string | null;
    linkedAt: string;
    lastSignInAt: string | null;
}

function serverBase(baseUrl: string): string {
    return baseUrl.replace(/\/+$/, '').replace(/\/api$/, '');
}

export const externalSignInService = {
    getProviders: async (baseUrl: string): Promise<SignInProviders> => {
        const response = await createDirectClient(baseUrl).get<SignInProviders>('/auth/providers');
        return response.data;
    },

    getProvidersFor: async (serverId?: string): Promise<SignInProviders> => {
        const response = await apiClient.get<SignInProviders>('/auth/providers', { serverId });
        return response.data;
    },

    startUrl: (baseUrl: string, slug: string, invite?: string): string => {
        const query = invite ? `?invite=${encodeURIComponent(invite)}` : '';
        return `${serverBase(baseUrl)}/api/auth/oidc/${encodeURIComponent(slug)}/start${query}`;
    },

    exchange: async (baseUrl: string, code: string): Promise<AuthResponse> => {
        const response = await createDirectClient(baseUrl).post<AuthResponse>('/auth/external/exchange', { code });
        return response.data;
    },

    getPending: async (baseUrl: string, handle: string): Promise<ExternalPending> => {
        const response = await createDirectClient(baseUrl).get<ExternalPending>(`/auth/external/pending/${encodeURIComponent(handle)}`);
        return response.data;
    },

    complete: async (baseUrl: string, request: ExternalCompleteRequest): Promise<ExternalComplete> => {
        const response = await createDirectClient(baseUrl).post<ExternalComplete>('/auth/external/complete', request);
        return response.data;
    },

    listLinked: async (serverId?: string): Promise<LinkedSignIn[]> => {
        const response = await apiClient.get<LinkedSignIn[]>('/auth/identities', { serverId });
        return response.data;
    },

    unlink: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/auth/identities/${id}`, { serverId });
    },

    startLink: async (slug: string, serverId?: string): Promise<string> => {
        const response = await apiClient.post<{ url: string }>(`/auth/oidc/${encodeURIComponent(slug)}/link`, {}, { serverId });
        return response.data.url;
    },
};
