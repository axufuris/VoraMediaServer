import { apiClient } from '../client';

export type EmailTemplateKey = 'PasswordReset' | 'AdminInvite' | 'RequestAvailable' | 'TestEmail';
export type EmailDeliveryStatus = 'Queued' | 'Sent' | 'Failed' | 'Dropped';

export interface EmailSettings {
    emailEnabled: boolean;
    smtpHost: string | null;
    smtpPort: number;
    smtpUseStartTls: boolean;
    smtpUseImplicitSsl: boolean;
    smtpUsername: string | null;
    smtpPasswordIsSet: boolean;
    smtpFromAddress: string | null;
    smtpFromDisplayName: string | null;
    emailPublicBaseUrl: string | null;
}

export interface UpdateEmailSettingsRequest {
    emailEnabled: boolean;
    smtpHost: string | null;
    smtpPort: number;
    smtpUseStartTls: boolean;
    smtpUseImplicitSsl: boolean;
    smtpUsername: string | null;
    newSmtpPassword: string | null;
    clearSmtpPassword: boolean;
    smtpFromAddress: string | null;
    smtpFromDisplayName: string | null;
    emailPublicBaseUrl: string | null;
}

export interface SendTestEmailResponse {
    success: boolean;
    message: string | null;
}

export interface EmailTemplateSummary {
    key: EmailTemplateKey;
    displayName: string;
    description: string;
    hasOverride: boolean;
    overrideUpdatedAt: string | null;
}

export interface EmailTemplateVariable {
    name: string;
    description: string;
}

export interface EmailTemplateDetail {
    key: EmailTemplateKey;
    displayName: string;
    description: string;
    defaultSubject: string;
    defaultHtmlBody: string;
    defaultTextBody: string;
    subjectOverride: string | null;
    htmlBodyOverride: string | null;
    textBodyOverride: string | null;
    hasOverride: boolean;
    overrideUpdatedAt: string | null;
    availableVariables: EmailTemplateVariable[];
}

export interface UpdateEmailTemplateRequest {
    subjectOverride: string | null;
    htmlBodyOverride: string | null;
    textBodyOverride: string | null;
}

export interface EmailDeliveryLogEntry {
    id: string;
    templateKey: EmailTemplateKey;
    toAddress: string;
    subject: string;
    status: EmailDeliveryStatus;
    attemptCount: number;
    errorMessage: string | null;
    createdAt: string;
    sentAt: string | null;
}

export const emailAdminService = {
    getSettings: async (serverId?: string): Promise<EmailSettings> => {
        const response = await apiClient.get<EmailSettings>('/email/settings', { serverId });
        return response.data;
    },
    updateSettings: async (settings: UpdateEmailSettingsRequest, serverId?: string): Promise<void> => {
        await apiClient.put('/email/settings', settings, { serverId });
    },
    sendTest: async (toAddress: string, serverId?: string): Promise<SendTestEmailResponse> => {
        const response = await apiClient.post<SendTestEmailResponse>('/email/test', { toAddress }, { serverId });
        return response.data;
    },
    listTemplates: async (serverId?: string): Promise<EmailTemplateSummary[]> => {
        const response = await apiClient.get<EmailTemplateSummary[]>('/email/templates', { serverId });
        return response.data;
    },
    getTemplate: async (key: EmailTemplateKey, serverId?: string): Promise<EmailTemplateDetail> => {
        const response = await apiClient.get<EmailTemplateDetail>(`/email/templates/${key}`, { serverId });
        return response.data;
    },
    updateTemplate: async (key: EmailTemplateKey, request: UpdateEmailTemplateRequest, serverId?: string): Promise<void> => {
        await apiClient.put(`/email/templates/${key}`, request, { serverId });
    },
    deleteTemplate: async (key: EmailTemplateKey, serverId?: string): Promise<void> => {
        await apiClient.delete(`/email/templates/${key}`, { serverId });
    },
    getLog: async (take = 50, serverId?: string): Promise<EmailDeliveryLogEntry[]> => {
        const response = await apiClient.get<EmailDeliveryLogEntry[]>(`/email/log?take=${take}`, { serverId });
        return response.data;
    }
};
