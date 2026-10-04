import type { ServerSettings } from '../../../api/System/systemSettingsAdminService';
import type { FeatureFlagsVM } from '../../../api/System/featureFlagsService';
import type { PluginVM } from '../../../api/System/pluginAdminService';
import type { DiscoveryRowConfig } from '../../../api/Discovery/discoveryService';
import type { SetupContent, SetupStepId } from './setupSteps';

export interface SetupStepProps {
    serverId?: string;
    settings: ServerSettings;
    onSettings: (patch: Partial<ServerSettings>) => void;
    features: FeatureFlagsVM;
    onFeatures: (patch: Partial<FeatureFlagsVM>) => void;
    content: SetupContent;
    onContent: (patch: Partial<SetupContent>) => void;
    plugins: PluginVM[];
    onPluginsChanged: () => void;
    discoverRows: DiscoveryRowConfig[];
    onDiscoverRows: (rows: DiscoveryRowConfig[]) => void;
    hardwareDevices: string[];
    goTo: (id: SetupStepId) => void;
}

export const findPlugin = (plugins: PluginVM[], id: string): PluginVM | undefined => plugins.find(p => p.id === id);

export const hasKeySaved = (plugins: PluginVM[], id: string): boolean => {
    const plugin = findPlugin(plugins, id);
    return !!plugin && plugin.isEnabled && !plugin.requiresConfiguration;
};

export const PLUGIN_FIELDS = {
    tmdb: ['api_key'],
    tvdb: ['api_key', 'subscriber_pin'],
    fanart: ['api_key'],
    mal: ['client_id'],
    omdb: ['api_key'],
    openSubtitles: ['api_key', 'default_languages'],
    lastFm: ['api_key', 'api_secret'],
    genius: ['access_token'],
    openAi: ['api_key', 'monthly_token_limit'],
} as const satisfies Record<string, readonly string[]>;
