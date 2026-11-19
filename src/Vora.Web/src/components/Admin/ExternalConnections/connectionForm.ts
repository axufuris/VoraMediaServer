import axios from 'axios';
import type { WebhookChangeKind, WebhookSettingDefinition } from '../../../api/System/webhookAdminService';
import { resolveReason } from '../../../utils/apiError';

type Caught = Parameters<typeof resolveReason>[0];

export interface OptionChoice {
    value: string;
    label: string;
}

export function parseOption(option: string): OptionChoice {
    const at = option.indexOf('=');
    return at < 0 ? { value: option, label: option } : { value: option.slice(0, at), label: option.slice(at + 1) };
}

export function isSettingVisible(definition: WebhookSettingDefinition, values: Record<string, string>): boolean {
    const rule = definition.showWhen?.trim();
    if (!rule) return true;
    const at = rule.indexOf('=');
    if (at < 0) return true;
    return (values[rule.slice(0, at).trim()] ?? '') === rule.slice(at + 1).trim();
}

export function initialSettings(definitions: WebhookSettingDefinition[], existing?: Record<string, string>): Record<string, string> {
    const values: Record<string, string> = {};
    for (const definition of definitions) {
        values[definition.key] = existing?.[definition.key] ?? definition.defaultValue ?? '';
    }
    return values;
}

export function hasMissingRequired(definitions: WebhookSettingDefinition[], values: Record<string, string>): boolean {
    return definitions.some(definition => definition.required && isSettingVisible(definition, values) && !(values[definition.key] ?? '').trim());
}

export function setupSteps(instructions: string): string[] {
    return (instructions ?? '').split('\n').map(step => step.trim()).filter(step => step.length > 0);
}

export function webhookErrorMessage(err: Caught, fallback: string): string {
    if (axios.isAxiosError<{ error?: string }>(err)) {
        const message = err.response?.data?.error;
        if (typeof message === 'string' && message.trim()) return message.trim();
    }
    const reason = resolveReason(err);
    return reason ? `${fallback} ${reason}` : fallback;
}

const CHANGE_KIND_LABELS: Record<WebhookChangeKind, string> = {
    Added: 'Added',
    Removed: 'Removed',
    FolderRemoved: 'Folder removed',
    ScanLibrary: 'Scan library'
};

export function changeKindLabel(kind: WebhookChangeKind): string {
    return CHANGE_KIND_LABELS[kind] ?? kind;
}
