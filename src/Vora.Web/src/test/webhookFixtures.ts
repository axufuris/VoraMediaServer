import type {
    WebhookConnectionVM,
    WebhookConnectionWithSecret,
    WebhookDeliveryVM,
    WebhookRequestServerVM,
    WebhookSettingDefinition,
    WebhookTemplateVM
} from '../api/System/webhookAdminService';

export const FAKE_SECRET = 'fake-secret-for-tests';

export const FOCUS_FILL = [
    'focus-visible:bg-[var(--vora-accent-500)]!',
    'focus-visible:text-[var(--vora-accent-contrast)]!',
    'focus-visible:outline-none!'
];

export const FOCUS_FILL_PRIMARY = [
    'focus-visible:bg-[var(--vora-accent-active)]!',
    'focus-visible:text-[var(--vora-accent-contrast)]!',
    'focus-visible:outline-none!'
];

export const minutesAgo = (minutes: number): string => new Date(Date.now() - minutes * 60_000).toISOString();

const setting = (overrides: Partial<WebhookSettingDefinition> & Pick<WebhookSettingDefinition, 'key' | 'label' | 'type'>): WebhookSettingDefinition => ({
    defaultValue: '',
    description: '',
    placeholder: '',
    required: false,
    options: [],
    showWhen: '',
    ...overrides
});

export const radarrTemplate: WebhookTemplateVM = {
    id: 'radarr_webhook',
    name: 'Radarr',
    description: 'Updates movie libraries when Radarr changes a file.',
    defaultConnectionName: 'Radarr',
    setupInstructions: 'In Radarr, go to Settings, Connect, and add a Webhook.\nPaste the address into the URL field.\nSet Password to the secret.',
    libraryKind: 'Movie',
    requestServerProviderId: 'radarr_requester',
    settings: []
};

export const sonarrTemplate: WebhookTemplateVM = {
    id: 'sonarr_webhook',
    name: 'Sonarr',
    description: 'Updates TV libraries when Sonarr changes a file.',
    defaultConnectionName: 'Sonarr',
    setupInstructions: 'In Sonarr, go to Settings, Connect, and add a Webhook.',
    libraryKind: 'TvShow',
    requestServerProviderId: 'sonarr_requester',
    settings: []
};

export const lidarrTemplate: WebhookTemplateVM = {
    id: 'lidarr_webhook',
    name: 'Lidarr',
    description: 'Updates music libraries when Lidarr changes a file.',
    defaultConnectionName: 'Lidarr',
    setupInstructions: 'In Lidarr, go to Settings, Connect, and add a Webhook.',
    libraryKind: 'Music',
    requestServerProviderId: null,
    settings: []
};

export const customTemplate: WebhookTemplateVM = {
    id: 'custom_webhook',
    name: 'Custom',
    description: 'Any system that can send a webhook.',
    defaultConnectionName: 'Custom webhook',
    setupInstructions: 'Send a POST to the address.\nUse the secret as the password.',
    libraryKind: null,
    requestServerProviderId: null,
    settings: [
        setting({ key: 'pathField', label: 'Path field', type: 'text', required: true, placeholder: 'file.path', description: 'The JSON field that holds the file path.' }),
        setting({ key: 'mode', label: 'Reports', type: 'select', defaultValue: 'file', options: ['file=One file', 'folder=A whole folder'], description: 'What each delivery names.' }),
        setting({ key: 'folderDepth', label: 'Folder depth', type: 'text', required: true, showWhen: 'mode=folder', defaultValue: '1' }),
        setting({ key: 'library', label: 'Library', type: 'library', required: true, description: 'Where the files land.' }),
        setting({ key: 'notes', label: 'Notes', type: 'textarea', placeholder: 'Anything to remember' })
    ]
};

export const allTemplates = (): WebhookTemplateVM[] => [radarrTemplate, sonarrTemplate, lidarrTemplate, customTemplate];

export const makeConnection = (overrides: Partial<WebhookConnectionVM> = {}): WebhookConnectionVM => ({
    id: 'c1',
    name: 'Movies 4K',
    providerId: 'radarr_webhook',
    providerName: 'Radarr',
    isEnabled: true,
    handleDeletes: true,
    receivePath: '/api/webhooks/c1',
    pathMappings: '/movies => /media/movies',
    settings: {},
    createdAt: '2026-11-01T10:00:00Z',
    lastDeliveryAt: null,
    lastDeliveryOk: null,
    ...overrides
});

export const withSecret = (connection: WebhookConnectionVM, secret = FAKE_SECRET): WebhookConnectionWithSecret => ({ ...connection, secret });

export const makeDelivery = (overrides: Partial<WebhookDeliveryVM> = {}): WebhookDeliveryVM => ({
    id: 'd1',
    at: '2026-11-18T10:15:00Z',
    connectionId: 'c1',
    connectionName: 'Movies 4K',
    providerId: 'radarr_webhook',
    instance: null,
    event: 'Download',
    path: '/movies/Heat (1995)/Heat (1995).mkv',
    outcome: 'Scan queued',
    ok: true,
    ...overrides
});

export const requestServers: WebhookRequestServerVM[] = [
    { id: 'rs1', name: 'Radarr 4K', providerId: 'radarr_requester', webhookProviderId: 'radarr_webhook' },
    { id: 'rs2', name: 'Sonarr main', providerId: 'sonarr_requester', webhookProviderId: 'sonarr_webhook' }
];
