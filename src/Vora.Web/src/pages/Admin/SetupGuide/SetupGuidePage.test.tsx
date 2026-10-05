import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import SetupGuidePage from './SetupGuidePage';
import type { SetupGuideVM } from '../../../api/System/setupGuideService';
import type { ServerSettings, PluginSettingField } from '../../../api/System/systemSettingsAdminService';
import { DEFAULT_FEATURE_FLAGS, type FeatureFlagsVM } from '../../../api/System/featureFlagsService';
import type { PluginVM } from '../../../api/System/pluginAdminService';
import type { DiscoveryRowConfig } from '../../../api/Discovery/discoveryService';
import type { EmailSettings } from '../../../api/System/emailAdminService';
import type { RemoteAccessStatus, UpdateRemoteAccessRequest } from '../../../api/System/remoteAccessService';
import type { BackupSettingsVM } from '../../../api/System/backupsService';

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
    updateRegistrationMode: vi.fn(),
    getEmailSettings: vi.fn(),
    updateEmailSettings: vi.fn(),
    sendTestEmail: vi.fn(),
    getRemoteStatus: vi.fn(),
    updateRemote: vi.fn(),
    getBackupSettings: vi.fn(),
    updateBackupSettings: vi.fn(),
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
        updateRegistrationMode: mocks.updateRegistrationMode,
    },
}));
vi.mock('../../../api/System/emailAdminService', () => ({ emailAdminService: { getSettings: mocks.getEmailSettings, updateSettings: mocks.updateEmailSettings, sendTest: mocks.sendTestEmail } }));
vi.mock('../../../api/System/remoteAccessService', () => ({ remoteAccessService: { getRemoteAccessStatus: mocks.getRemoteStatus, updateRemoteAccess: mocks.updateRemote } }));
vi.mock('../../../api/System/backupsService', () => ({ backupsService: { getSettings: mocks.getBackupSettings, updateSettings: mocks.updateBackupSettings } }));
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

const settings = { serverName: 'Vora QA', metadataLanguage: 'eng', scheduleTimeZone: '', streamingProfile: 1, useHardwareAcceleration: true, useHardwareEncoding: true, hardwareTranscodingDevice: 'Auto', runDetections: 0, detectionScheduleTime: '03:00', analyzeUseHardwareDecode: true, videoThumbnailGeneration: 0, videoThumbnailScheduleTime: '04:00', videoThumbnailUseHardwareDecode: true, enableAiMusicPlaylists: false, registrationMode: 2, preExtractSubtitlesOnScan: true } as ServerSettings;

const email: EmailSettings = { emailEnabled: false, smtpHost: null, smtpPort: 587, smtpUseStartTls: true, smtpUseImplicitSsl: false, smtpUsername: null, smtpPasswordIsSet: false, smtpFromAddress: null, smtpFromDisplayName: null, emailPublicBaseUrl: null };

const remote: RemoteAccessStatus = { isEnabled: false, upnpSupported: false, localIp: '192.168.1.20', localPort: 8080, publicIp: '203.0.113.5', publicPort: 32080, manuallySpecifyPort: false, externalUrl: null, reachable: false, accessUrl: '', errorMessage: '' };

const backups: BackupSettingsVM = { autoBackupEnabled: false, cadence: 'Daily', hour: 3, minute: 0, dayOfWeek: 'Sunday', dayOfMonth: 1, maxToKeep: 10, effectiveDirectory: '/app/data/backups', availableSections: [] };

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
        mocks.updateRegistrationMode.mockResolvedValue(undefined);
        mocks.getEmailSettings.mockResolvedValue({ ...email });
        mocks.updateEmailSettings.mockResolvedValue(undefined);
        mocks.getRemoteStatus.mockResolvedValue({ ...remote });
        mocks.updateRemote.mockImplementation((request: UpdateRemoteAccessRequest) => Promise.resolve({ ...remote, ...request, reachable: true }));
        mocks.getBackupSettings.mockResolvedValue({ ...backups });
        mocks.updateBackupSettings.mockImplementation((saved: BackupSettingsVM) => Promise.resolve(saved));
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
        expect(mocks.saveGuide).toHaveBeenLastCalledWith(expect.objectContaining({ liveTv: true, music: false, step: 'remote' }), undefined);
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

    it('saves the address of a reverse proxy as the remote access URL', async () => {
        mocks.getGuide.mockResolvedValue(guide({ step: 'remote' }));
        renderGuide();

        fireEvent.click(await screen.findByRole('radio', { name: /Reverse proxy or tunnel/ }));
        fireEvent.change(screen.getByLabelText('Public address'), { target: { value: 'vora.example.com/' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save and continue →' }));

        await waitFor(() => expect(mocks.updateRemote).toHaveBeenCalledWith(expect.objectContaining({ isEnabled: true, externalUrl: 'https://vora.example.com' }), undefined));
        expect(await screen.findByRole('heading', { name: 'Sign-ups' })).toBeInTheDocument();
    });

    it("stays on remote access when the address can't be used", async () => {
        mocks.getGuide.mockResolvedValue(guide({ step: 'remote' }));
        renderGuide();

        fireEvent.click(await screen.findByRole('radio', { name: /Reverse proxy or tunnel/ }));
        fireEvent.change(screen.getByLabelText('Public address'), { target: { value: 'not a url' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save and continue →' }));

        expect(await screen.findByRole('alert')).toHaveTextContent('Enter the full web address');
        expect(mocks.updateRemote).not.toHaveBeenCalled();
        expect(screen.getByRole('heading', { name: 'Remote access' })).toBeInTheDocument();
    });

    it('adds the email step for invitations and saves the sign-up mode', async () => {
        mocks.getGuide.mockResolvedValue(guide({ step: 'signup' }));
        renderGuide();

        await screen.findByRole('heading', { name: 'Sign-ups' });
        const rail = screen.getByRole('complementary', { name: 'Setup steps' });
        expect(rail).not.toHaveTextContent('Email');

        fireEvent.click(screen.getByRole('radio', { name: /Email invitation/ }));
        expect(rail).toHaveTextContent('Email');
        fireEvent.click(screen.getByRole('button', { name: 'Save and continue →' }));

        await waitFor(() => expect(mocks.updateRegistrationMode).toHaveBeenCalledWith(3, undefined));
        expect(await screen.findByRole('heading', { name: 'Email' })).toBeInTheDocument();
    });

    it('offers the email step with the other sign-up modes too', async () => {
        mocks.getGuide.mockResolvedValue(guide({ step: 'signup' }));
        renderGuide();

        fireEvent.click(await screen.findByRole('switch', { name: 'Set up email too' }));

        expect(screen.getByRole('complementary', { name: 'Setup steps' })).toHaveTextContent('Email');
        fireEvent.click(screen.getByRole('button', { name: 'Save and continue →' }));
        expect(await screen.findByRole('heading', { name: 'Email' })).toBeInTheDocument();
        expect(mocks.updateRegistrationMode).not.toHaveBeenCalled();
    });

    it("fills in the provider's server and saves email with the public address from remote access", async () => {
        mocks.getGuide.mockResolvedValue(guide({ step: 'email' }));
        mocks.getServerSettings.mockResolvedValue({ ...settings, registrationMode: 3 });
        mocks.getRemoteStatus.mockResolvedValue({ ...remote, isEnabled: true, externalUrl: 'https://vora.example.com' });
        renderGuide();

        expect(await screen.findByLabelText('Public base URL')).toHaveValue('https://vora.example.com');
        fireEvent.click(screen.getByRole('button', { name: 'Gmail' }));
        fireEvent.click(screen.getByRole('button', { name: 'Fill in server settings' }));
        expect(screen.getByLabelText('SMTP server')).toHaveValue('smtp.gmail.com');
        fireEvent.change(screen.getByLabelText('Username'), { target: { value: 'vora@example.com' } });
        fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'example-password' } });
        fireEvent.change(screen.getByLabelText('From address'), { target: { value: 'vora@example.com' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save and continue →' }));

        await waitFor(() => expect(mocks.updateEmailSettings).toHaveBeenCalledWith(expect.objectContaining({
            emailEnabled: true,
            smtpHost: 'smtp.gmail.com',
            smtpPort: 587,
            smtpUseStartTls: true,
            smtpUseImplicitSsl: false,
            smtpUsername: 'vora@example.com',
            newSmtpPassword: 'example-password',
            smtpFromAddress: 'vora@example.com',
            emailPublicBaseUrl: 'https://vora.example.com',
        }), undefined));
    });

    it('needs an SMTP server before moving on with email turned on', async () => {
        mocks.getGuide.mockResolvedValue(guide({ step: 'email' }));
        mocks.getServerSettings.mockResolvedValue({ ...settings, registrationMode: 3 });
        renderGuide();

        fireEvent.change(await screen.findByLabelText('From address'), { target: { value: 'me@example.com' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save and continue →' }));

        expect(await screen.findByRole('alert')).toHaveTextContent("Enter your email provider's SMTP server");
        expect(mocks.updateEmailSettings).not.toHaveBeenCalled();
    });

    it('turns on scheduled backups', async () => {
        mocks.getGuide.mockResolvedValue(guide({ step: 'backups' }));
        renderGuide();

        fireEvent.click(await screen.findByRole('switch', { name: 'Back up automatically' }));
        fireEvent.change(screen.getByLabelText('At'), { target: { value: '02:30' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save and continue →' }));

        await waitFor(() => expect(mocks.updateBackupSettings).toHaveBeenCalledWith(expect.objectContaining({ autoBackupEnabled: true, cadence: 'Daily', hour: 2, minute: 30 }), undefined));
        expect(await screen.findByRole('heading', { name: 'The groundwork is set' })).toBeInTheDocument();
    });

    it('remembers this session so the pop-up does not follow the admin out of the guide', async () => {
        mocks.getGuide.mockResolvedValue(guide({ step: 'server' }));
        renderGuide();

        await screen.findByRole('heading', { name: 'Your server' });
        expect(sessionStorage.getItem('setup_guide_prompted_local')).toBe('true');
    });
});
