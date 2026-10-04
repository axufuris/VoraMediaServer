import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import SetupGuidePage from './SetupGuidePage';
import type { SetupGuideVM } from '../../../api/System/setupGuideService';
import type { ServerSettings, PluginSettingField } from '../../../api/System/systemSettingsAdminService';
import { DEFAULT_FEATURE_FLAGS, type FeatureFlagsVM } from '../../../api/System/featureFlagsService';
import type { PluginVM } from '../../../api/System/pluginAdminService';
import type { DiscoveryRowConfig } from '../../../api/Discovery/discoveryService';

const mocks = vi.hoisted(() => ({
    getGuide: vi.fn(),
    saveGuide: vi.fn(),
    getServerSettings: vi.fn(),
    updateServerSettings: vi.fn(),
    getPluginSettings: vi.fn(),
    updatePluginSettings: vi.fn(),
    testPluginConnection: vi.fn(),
    getFeatureFlags: vi.fn(),
    updateFeatureFlags: vi.fn(),
    getPlugins: vi.fn(),
    getAdminConfigs: vi.fn(),
    updateAdminConfigs: vi.fn(),
    confirm: vi.fn(),
}));

vi.mock('../../../api/System/setupGuideService', () => ({ setupGuideService: { get: mocks.getGuide, save: mocks.saveGuide } }));
vi.mock('../../../api/System/systemSettingsAdminService', () => ({
    systemSettingsAdminService: {
        getServerSettings: mocks.getServerSettings,
        updateServerSettings: mocks.updateServerSettings,
        getHardwareDevices: () => Promise.resolve(['Auto', '/dev/dri/renderD128']),
        getPluginSettings: mocks.getPluginSettings,
        updatePluginSettings: mocks.updatePluginSettings,
        testPluginConnection: mocks.testPluginConnection,
    },
}));
vi.mock('../../../api/System/featureFlagsService', async importOriginal => ({
    ...(await importOriginal<typeof import('../../../api/System/featureFlagsService')>()),
    featureFlagsService: { getFeatureFlags: mocks.getFeatureFlags, updateFeatureFlags: mocks.updateFeatureFlags },
}));
vi.mock('../../../api/System/pluginAdminService', () => ({ pluginAdminService: { getPlugins: mocks.getPlugins } }));
vi.mock('../../../api/Discovery/discoveryService', () => ({ discoveryService: { getAdminConfigs: mocks.getAdminConfigs, updateAdminConfigs: mocks.updateAdminConfigs } }));
vi.mock('../../../api/Discovery/requestAdminService', () => ({ requestAdminService: { getServers: () => Promise.resolve([]), getProviderOptions: () => Promise.resolve([]), saveServer: () => Promise.resolve() } }));
vi.mock('../../../dialogs', () => ({ useDialog: () => ({ confirm: mocks.confirm, alert: () => Promise.resolve(), prompt: () => Promise.resolve(null) }) }));

const guide = (overrides: Partial<SetupGuideVM> = {}): SetupGuideVM => ({
    status: 'InProgress', step: 'welcome', moviesAndShows: true, music: true, liveTv: false, internetRadio: false, podcasts: false, ...overrides,
});

const settings = { serverName: 'Vora QA', metadataLanguage: 'eng', scheduleTimeZone: '', streamingProfile: 1, useHardwareAcceleration: true, useHardwareEncoding: true, hardwareTranscodingDevice: 'Auto', runDetections: 0, detectionScheduleTime: '03:00', analyzeUseHardwareDecode: true, videoThumbnailGeneration: 0, videoThumbnailScheduleTime: '04:00', videoThumbnailUseHardwareDecode: true, enableAiMusicPlaylists: false } as ServerSettings;

const plugin = (id: string, requiresConfiguration = true): PluginVM => ({
    id, name: id, version: '1', description: '', isSystemPlugin: true, type: 'Metadata', hasSettings: true, isAiPlugin: false, isEnabled: true, requiresConfiguration, supportsConnectionTest: true,
});

const field = (key: string, label: string, value = ''): PluginSettingField => ({ key, label, type: key === 'api_key' ? 'password' : 'text', description: `How to get the ${label}.`, value, placeholder: '', required: key === 'api_key', options: [] });

const renderGuide = (path = '/admin/setup') => render(
    <MemoryRouter initialEntries={[path]}>
        <Routes>
            <Route path="/admin/setup" element={<SetupGuidePage />} />
            <Route path="/admin" element={<div>Dashboard page</div>} />
            <Route path="/admin/libraries/new" element={<div>Add library page</div>} />
        </Routes>
    </MemoryRouter>,
);

describe('SetupGuidePage', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        sessionStorage.clear();
        mocks.saveGuide.mockImplementation((g: SetupGuideVM) => Promise.resolve(g));
        mocks.getServerSettings.mockResolvedValue({ ...settings });
        mocks.updateServerSettings.mockResolvedValue(undefined);
        mocks.getFeatureFlags.mockResolvedValue({ ...DEFAULT_FEATURE_FLAGS });
        mocks.updateFeatureFlags.mockResolvedValue(undefined);
        mocks.getPlugins.mockResolvedValue([plugin('tmdb_metadata'), plugin('tvdb_metadata'), plugin('omdb_imdb')]);
        mocks.getAdminConfigs.mockResolvedValue([]);
        mocks.updatePluginSettings.mockResolvedValue(undefined);
        mocks.testPluginConnection.mockResolvedValue({ success: true, message: 'TMDB accepted the API key.' });
        mocks.getPluginSettings.mockImplementation((id: string) => Promise.resolve(
            id === 'tvdb_metadata' ? [field('api_key', 'TVDB API Key'), field('subscriber_pin', 'TVDB Subscriber PIN')] : [field('api_key', `${id} key`)]
        ));
    });

    it('starts a new guide, shows what the server has now, and saves each step', async () => {
        mocks.getGuide.mockResolvedValue(guide({ status: 'NotStarted', step: null }));
        renderGuide();

        await waitFor(() => expect(mocks.saveGuide).toHaveBeenCalledWith(expect.objectContaining({ status: 'InProgress', step: 'welcome' }), undefined));
        fireEvent.click(await screen.findByRole('button', { name: 'Start →' }));

        const name = await screen.findByLabelText('Server name');
        expect(name).toHaveValue('Vora QA');
        fireEvent.change(name, { target: { value: 'Living Room' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save and continue →' }));

        await waitFor(() => expect(mocks.updateServerSettings).toHaveBeenCalledWith(expect.objectContaining({ serverName: 'Living Room', useHardwareAcceleration: true }), undefined));
        expect(mocks.saveGuide).toHaveBeenLastCalledWith(expect.objectContaining({ step: 'playback', status: 'InProgress' }), undefined);
        expect(await screen.findByRole('heading', { name: 'Playback' })).toBeInTheDocument();
    });

    it('opens where the admin left off', async () => {
        mocks.getGuide.mockResolvedValue(guide({ step: 'ratings' }));
        renderGuide();

        expect(await screen.findByRole('heading', { name: 'Ratings' })).toBeInTheDocument();
        expect(screen.getByText(/1,000 lookups a day/)).toBeInTheDocument();
    });

    it('builds the remaining sections from what the admin will put on the server', async () => {
        mocks.getGuide.mockResolvedValue(guide({ step: 'content' }));
        renderGuide();

        fireEvent.click(await screen.findByRole('button', { name: /^Live TV/ }));
        fireEvent.click(screen.getByRole('button', { name: /^Music/ }));

        const rail = screen.getByRole('complementary', { name: 'Setup steps' });
        expect(rail).toHaveTextContent('Live TV');
        expect(rail).not.toHaveTextContent('Last.fm');

        fireEvent.click(screen.getByRole('button', { name: 'Save and continue →' }));
        await waitFor(() => expect(mocks.updateFeatureFlags).toHaveBeenCalledWith(expect.objectContaining<Partial<FeatureFlagsVM>>({ liveTvEnabled: true }), undefined));
        expect(mocks.saveGuide).toHaveBeenLastCalledWith(expect.objectContaining({ liveTv: true, music: false, step: 'metadata' }), undefined);
    });

    it('saves a key that was typed but not saved when the admin moves on', async () => {
        mocks.getGuide.mockResolvedValue(guide({ step: 'metadata' }));
        renderGuide();

        fireEvent.change(await screen.findByLabelText('tmdb_metadata key'), { target: { value: 'abc123' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save and continue →' }));

        await waitFor(() => expect(mocks.updatePluginSettings).toHaveBeenCalledWith('tmdb_metadata', { api_key: 'abc123', is_enabled: 'true' }, undefined));
        expect(await screen.findByRole('heading', { name: 'Artwork' })).toBeInTheDocument();
    });

    it('adds Discover once a plugin offers rows', async () => {
        const rows: DiscoveryRowConfig[] = [{ id: '1', rowId: 'movie_popular', providerId: 'tmdb_discovery', name: 'Popular Movies', providerName: 'TMDB', orderIndex: 0, isEnabled: false }];
        mocks.getAdminConfigs.mockResolvedValue(rows);
        mocks.getGuide.mockResolvedValue(guide({ step: 'discover' }));
        renderGuide();

        expect(await screen.findByRole('heading', { name: 'Discover' })).toBeInTheDocument();
        fireEvent.click(screen.getByRole('switch', { name: 'Show Popular Movies' }));
        fireEvent.click(screen.getByRole('button', { name: 'Save and continue →' }));

        await waitFor(() => expect(mocks.updateAdminConfigs).toHaveBeenCalledWith([expect.objectContaining({ rowId: 'movie_popular', isEnabled: true })], undefined));
    });

    it('asks before skipping, then never opens by itself again', async () => {
        mocks.getGuide.mockResolvedValue(guide({ step: 'server' }));
        mocks.confirm.mockResolvedValue(true);
        renderGuide();

        fireEvent.click(await screen.findByRole('button', { name: 'Skip setup' }));

        expect(await screen.findByText('Dashboard page')).toBeInTheDocument();
        expect(mocks.saveGuide).toHaveBeenLastCalledWith(expect.objectContaining({ status: 'Skipped' }), undefined);
    });

    it('stays put when the admin backs out of skipping', async () => {
        mocks.getGuide.mockResolvedValue(guide({ step: 'server' }));
        mocks.confirm.mockResolvedValue(false);
        renderGuide();

        fireEvent.click(await screen.findByRole('button', { name: 'Skip setup' }));

        await waitFor(() => expect(mocks.confirm).toHaveBeenCalled());
        expect(screen.getByRole('heading', { name: 'Your server' })).toBeInTheDocument();
        expect(mocks.saveGuide).not.toHaveBeenCalledWith(expect.objectContaining({ status: 'Skipped' }), undefined);
    });

    it('finishes by marking the guide complete and opening Add library', async () => {
        mocks.getGuide.mockResolvedValue(guide({ step: 'done' }));
        mocks.getPluginSettings.mockResolvedValue([field('api_key', 'OpenAI key')]);
        renderGuide();

        expect(await screen.findByRole('heading', { name: 'The groundwork is set' })).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Add your first library →' }));

        expect(await screen.findByText('Add library page')).toBeInTheDocument();
        expect(mocks.saveGuide).toHaveBeenLastCalledWith(expect.objectContaining({ status: 'Completed', step: 'done' }), undefined);
    });

    it('remembers this session so the pop-up does not follow the admin out of the guide', async () => {
        mocks.getGuide.mockResolvedValue(guide({ step: 'server' }));
        renderGuide();

        await screen.findByRole('heading', { name: 'Your server' });
        expect(sessionStorage.getItem('setup_guide_prompted_local')).toBe('true');
    });
});
