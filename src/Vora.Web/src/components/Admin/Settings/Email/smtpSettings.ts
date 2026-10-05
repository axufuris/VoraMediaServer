import type { EmailSettings, UpdateEmailSettingsRequest } from '../../../../api/System/emailAdminService';

export type TlsMode = 'startTls' | 'implicitSsl' | 'none';

export interface SmtpPasswordDraft {
    value: string;
    clear: boolean;
    editing: boolean;
}

export const EMPTY_PASSWORD_DRAFT: SmtpPasswordDraft = { value: '', clear: false, editing: false };

export function tlsModeFromSettings(settings: EmailSettings): TlsMode {
    if (settings.smtpUseImplicitSsl) return 'implicitSsl';
    if (settings.smtpUseStartTls) return 'startTls';
    return 'none';
}

export function toEmailUpdate(settings: EmailSettings, tlsMode: TlsMode, password: SmtpPasswordDraft): UpdateEmailSettingsRequest {
    return {
        emailEnabled: settings.emailEnabled,
        smtpHost: settings.smtpHost,
        smtpPort: settings.smtpPort,
        smtpUseStartTls: tlsMode === 'startTls',
        smtpUseImplicitSsl: tlsMode === 'implicitSsl',
        smtpUsername: settings.smtpUsername,
        newSmtpPassword: password.clear ? null : (password.value.length > 0 ? password.value : null),
        clearSmtpPassword: password.clear,
        smtpFromAddress: settings.smtpFromAddress,
        smtpFromDisplayName: settings.smtpFromDisplayName,
        emailPublicBaseUrl: settings.emailPublicBaseUrl,
    };
}
