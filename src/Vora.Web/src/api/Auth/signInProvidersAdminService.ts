import { apiClient } from '../client';
import type { AuthProviderType } from './externalSignInService';

export type NewUserPolicy = 'FollowRegistrationMode' | 'IdpDecides' | 'ExistingOnly';
export type LinkByEmailPolicy = 'ConfirmWithPassword' | 'Automatic' | 'Never';
export type LdapSecurity = 'Ldaps' | 'StartTls' | 'None';

export interface AuthProviderAdmin {
    id: string;
    type: AuthProviderType;
    slug: string;
    displayName: string;
    enabled: boolean;
    iconUrl: string | null;
    sortOrder: number;
    issuer: string | null;
    clientId: string | null;
    hasSecret: boolean;
    scopes: string;
    emailClaim: string;
    nameClaim: string;
    groupsClaim: string;
    newUserPolicy: NewUserPolicy;
    linkByEmail: LinkByEmailPolicy;
    allowedEmailDomains: string[];
    requiredGroup: string | null;
    adminGroup: string | null;
    redirectUri: string;
    ldapHost: string | null;
    ldapPort: number | null;
    ldapSecurity: LdapSecurity;
    ldapCaCertificatePem: string | null;
    ldapBindDn: string | null;
    hasBindPassword: boolean;
    ldapUserBaseDn: string | null;
    ldapUserFilter: string | null;
    ldapMailAttribute: string;
    ldapDisplayNameAttribute: string;
    ldapUniqueIdAttribute: string;
    ldapGroupAttribute: string;
    ldapGroupBaseDn: string | null;
    ldapGroupFilter: string | null;
}

export interface SaveAuthProvider {
    type: AuthProviderType;
    slug: string;
    displayName: string;
    enabled: boolean;
    iconUrl: string | null;
    sortOrder: number;
    issuer?: string | null;
    clientId?: string | null;
    clientSecret?: string;
    clearClientSecret?: boolean;
    scopes?: string;
    emailClaim?: string;
    nameClaim?: string;
    groupsClaim?: string;
    newUserPolicy: NewUserPolicy;
    linkByEmail: LinkByEmailPolicy;
    allowedEmailDomains: string[];
    requiredGroup: string | null;
    adminGroup: string | null;
    ldapHost?: string | null;
    ldapPort?: number | null;
    ldapSecurity?: LdapSecurity;
    ldapCaCertificatePem?: string | null;
    ldapBindDn?: string | null;
    ldapBindPassword?: string;
    clearLdapBindPassword?: boolean;
    ldapUserBaseDn?: string | null;
    ldapUserFilter?: string | null;
    ldapMailAttribute?: string;
    ldapDisplayNameAttribute?: string;
    ldapUniqueIdAttribute?: string;
    ldapGroupAttribute?: string;
    ldapGroupBaseDn?: string | null;
    ldapGroupFilter?: string | null;
}

export interface AuthSettings {
    passwordSignInEnabled: boolean;
    publicBaseUrl: string | null;
    effectiveBaseUrl: string;
}

export interface OidcTestResult {
    ok: boolean;
    message: string | null;
    issuer: string | null;
    authorizationEndpoint: string | null;
    tokenEndpoint: string | null;
    jwksUri: string | null;
    userInfoEndpoint: string | null;
    signingKeyCount: number;
    advertisesPkceS256: boolean;
    warnings: string[];
}

export interface LdapTestResult {
    ok: boolean;
    message: string | null;
    connected: boolean;
    serviceBound: boolean;
    entriesFound: number;
    searchLimited: boolean;
    entriesWithMail: number;
    entriesWithoutMail: number;
    entriesWithUniqueId: number;
    sampleDns: string[];
    warnings: string[];
}

export type ProviderTestResult = OidcTestResult | LdapTestResult;

export const signInProvidersAdminService = {
    list: async (serverId?: string): Promise<AuthProviderAdmin[]> => {
        const response = await apiClient.get<AuthProviderAdmin[]>('/admin/auth-providers', { serverId });
        return response.data;
    },

    create: async (provider: SaveAuthProvider, serverId?: string): Promise<AuthProviderAdmin> => {
        const response = await apiClient.post<AuthProviderAdmin>('/admin/auth-providers', provider, { serverId });
        return response.data;
    },

    update: async (id: string, provider: SaveAuthProvider, serverId?: string): Promise<AuthProviderAdmin> => {
        const response = await apiClient.put<AuthProviderAdmin>(`/admin/auth-providers/${id}`, provider, { serverId });
        return response.data;
    },

    remove: async (id: string, force: boolean, serverId?: string): Promise<void> => {
        await apiClient.delete(`/admin/auth-providers/${id}`, { serverId, params: force ? { force: true } : undefined });
    },

    test: async (id: string, serverId?: string): Promise<ProviderTestResult> => {
        const response = await apiClient.post<ProviderTestResult>(`/admin/auth-providers/${id}/test`, undefined, { serverId });
        return response.data;
    },

    getSettings: async (serverId?: string): Promise<AuthSettings> => {
        const response = await apiClient.get<AuthSettings>('/admin/auth-settings', { serverId });
        return response.data;
    },

    updateSettings: async (settings: { passwordSignInEnabled: boolean; publicBaseUrl: string | null }, serverId?: string): Promise<AuthSettings> => {
        const response = await apiClient.put<AuthSettings>('/admin/auth-settings', settings, { serverId });
        return response.data;
    },
};
