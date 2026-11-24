import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import MainLayout, { type NavItem } from './MainLayout';
import { serverVault } from '../utils/serverVault';
import { StorageKeys } from '../utils/storageKeys';
import { clearApiClientCache } from '../api/client';

const mocks = vi.hoisted(() => ({
    getNavPrefs: vi.fn(),
    saveNavPrefs: vi.fn(),
    getPlaybackSettings: vi.fn(),
    playbackCapabilities: vi.fn(),
}));

vi.mock('../api/Users/profileDeviceSettingsService', () => ({
    profileDeviceSettingsService: { getNavPrefs: mocks.getNavPrefs, saveNavPrefs: mocks.saveNavPrefs, getPlaybackSettings: mocks.getPlaybackSettings },
}));
vi.mock('../api/Media/libraryService', () => ({ libraryService: { getLibraries: () => Promise.resolve([]) } }));
vi.mock('../api/Auth/authService', () => ({ authService: { logout: vi.fn() } }));
vi.mock('../api/Streaming/streamingService', () => ({ streamingService: { measureConnection: () => Promise.resolve() } }));
vi.mock('../api/Users/deviceService', () => ({ deviceService: { updateCapabilities: () => Promise.resolve() } }));
vi.mock('../utils/desktopCapabilities', () => ({ playbackCapabilities: mocks.playbackCapabilities }));
vi.mock('../hooks/useSignalREvent', () => ({ useSignalREvent: () => { }, disconnectSignalR: () => { } }));
vi.mock('../hooks/useScrollRestoration', () => ({ useScrollRestoration: () => () => { } }));
vi.mock('../hooks/useFeatureFlags', () => ({
    useFeatureFlags: () => ({ discover: true, forYou: true, releaseCalendar: true, liveTv: true, dvr: true, internetRadio: true, podcasts: true }),
}));
vi.mock('../desktop/useDesktopPlayback', () => ({ useDesktopPlayback: () => ({ bridge: null }) }));
vi.mock('../desktop/desktopPlayback', () => ({ playsVideoInMpv: () => false, releaseBuiltInPlayer: () => { } }));
vi.mock('../contexts/usePlayer', () => ({ usePlayer: () => ({ currentMedia: null, isMinimized: false }) }));
vi.mock('../dialogs', () => ({
    useDialog: () => ({ alert: () => Promise.resolve(), confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) }),
}));
vi.mock('../components/Player/GlobalVideoPlayer', () => ({ default: () => null }));
vi.mock('../components/Player/DesktopPlaybackHost', () => ({ default: () => null }));
vi.mock('../components/Player/LiveTvPlayer', () => ({ default: () => null }));
vi.mock('../components/Player/LiveRadioPlayer', () => ({ default: () => null }));
vi.mock('../components/Player/NowPlayingFullscreen', () => ({ default: () => null }));
vi.mock('../components/Desktop/MpvMissingBanner', () => ({ default: () => null }));
vi.mock('../components/Layout/ServerManagerModal', () => ({ default: () => null }));
vi.mock('../components/Layout/PhoneNavDrawer', () => ({ default: ({ children }: { children: ReactNode }) => <>{children}</> }));
vi.mock('../components/Media/ItemMenu/MediaItemMenuProvider', () => ({ default: ({ children }: { children: ReactNode }) => <>{children}</> }));
vi.mock('./parts/MainLayoutUserMenu', () => ({ default: () => null }));
vi.mock('./parts/MainLayoutSidebar', () => ({
    default: ({ navItems, onTogglePin, collapsed, pin }: { navItems: NavItem[]; onTogglePin: (id: string, serverId?: string) => void; collapsed: boolean; pin?: { pinned: boolean; onToggle: () => void } }) => (
        <nav data-collapsed={String(collapsed)}>
            {navItems.map(item => <button key={`${item.serverId ?? ''}-${item.id}`} type="button" onClick={() => onTogglePin(item.id, item.serverId)}>{item.title}</button>)}
            {pin && <button type="button" aria-pressed={pin.pinned} onClick={pin.onToggle}>Pin sidebar</button>}
        </nav>
    ),
}));

const tokenFor = (profileId: string) => `header.${btoa(JSON.stringify({ sub: profileId }))}.signature`;

const renderAt = (path: string) => render(
    <MemoryRouter initialEntries={[path]}>
        <Routes>
            <Route path="/" element={<MainLayout />}>
                <Route index element={<div />} />
                <Route path="server/:serverId" element={<div />} />
                <Route path="media/:id" element={<div />} />
            </Route>
        </Routes>
    </MemoryRouter>,
);

beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    localStorage.setItem(StorageKeys.profileToken, tokenFor('profile-1'));
    localStorage.setItem(StorageKeys.deviceId, 'device-1');
    serverVault.addOrUpdateServer({ id: 'home', name: 'Home', url: 'http://home', token: tokenFor('profile-1'), profileId: 'profile-1', isAdmin: false });
    serverVault.addOrUpdateServer({ id: 'second', name: 'Second', url: 'http://second', token: tokenFor('profile-2'), profileId: 'profile-2', isAdmin: false });
    serverVault.setActiveServerId('home');
    mocks.getNavPrefs.mockResolvedValue(null);
    mocks.saveNavPrefs.mockResolvedValue(undefined);
    mocks.getPlaybackSettings.mockResolvedValue({ bitrate: 8, maxResolution: 1080, maxAudioChannels: 6, seekStepSeconds: 30, volumeStepPercent: 2, soundOnClick: true, soundOnNavOpen: true, soundOnMove: true });
    mocks.playbackCapabilities.mockResolvedValue({});
});

afterEach(() => {
    cleanup();
    vi.clearAllMocks();
    localStorage.clear();
    clearApiClientCache();
});

describe('MainLayout on a second server', () => {
    it("loads and saves the sidebar and playback prefs of that server's own profile", async () => {
        renderAt('/server/second');

        await waitFor(() => expect(mocks.getPlaybackSettings).toHaveBeenCalled());
        expect(mocks.getNavPrefs).toHaveBeenCalledWith('profile-2', 'device-1', 'second');
        expect(mocks.getPlaybackSettings).toHaveBeenCalledWith('profile-2', 'device-1', 'second');
        await waitFor(() => expect(localStorage.getItem('playback_prefs_profile-2_device-1')).toBe('{"bitrate":8,"maxResolution":1080,"maxAudioChannels":6,"seekStepSeconds":30,"volumeStepPercent":2}'));
        expect(localStorage.getItem('playback_prefs_profile-1_device-1')).toBeNull();

        fireEvent.click(await screen.findByRole('button', { name: 'Music' }));

        expect(mocks.saveNavPrefs).toHaveBeenCalledWith('profile-2', 'device-1', expect.any(String), 'second');
    });

    it('keeps the primary profile on the primary server', async () => {
        renderAt('/');

        await waitFor(() => expect(mocks.getPlaybackSettings).toHaveBeenCalled());
        expect(mocks.getNavPrefs).toHaveBeenCalledWith('profile-1', 'device-1', undefined);
        expect(mocks.getPlaybackSettings).toHaveBeenCalledWith('profile-1', 'device-1', undefined);

        fireEvent.click(await screen.findByRole('button', { name: 'Music' }));

        expect(mocks.saveNavPrefs).toHaveBeenCalledWith('profile-1', 'device-1', expect.any(String), undefined);
    });
});

describe('MainLayout identity on a second server', () => {
    const scheduledToken = (profileId: string) => {
        const today = new Date().getDay();
        const schedules = JSON.stringify([{ dayOfWeek: (today + 1) % 7, startTime: '00:00', endTime: '23:59' }]);
        return `header.${btoa(JSON.stringify({ sub: profileId, accountId: 'account-2', accessSchedules: schedules }))}.signature`;
    };

    it("blocks a second server's pages by that server's profile schedule", async () => {
        serverVault.addOrUpdateServer({ id: 'second', name: 'Second', url: 'http://second', token: scheduledToken('profile-2'), profileId: 'profile-2', isAdmin: false });

        renderAt('/server/second');

        expect(await screen.findByText('Outside Allowed Timeframe')).toBeInTheDocument();
    });

    it('leaves the primary server open when only the second profile has a schedule', async () => {
        serverVault.addOrUpdateServer({ id: 'second', name: 'Second', url: 'http://second', token: scheduledToken('profile-2'), profileId: 'profile-2', isAdmin: false });

        renderAt('/');

        expect(await screen.findByRole('button', { name: 'Music' })).toBeInTheDocument();
        expect(screen.queryByText('Outside Allowed Timeframe')).toBeNull();
    });

    it("reports this device's capabilities to a second server with that server's playback prefs", async () => {
        renderAt('/server/second');

        await waitFor(() => expect(mocks.playbackCapabilities).toHaveBeenCalledWith('second'));
    });
});

describe('MainLayout sidebar folding', () => {
    const installedApp = () => vi.stubGlobal('matchMedia', (query: string) => ({
        matches: query.includes('display-mode'),
        addEventListener: () => { },
        removeEventListener: () => { },
    }));

    const sidebar = () => screen.getByRole('navigation');

    afterEach(() => vi.unstubAllGlobals());

    it('leaves the fold to the viewer in a browser tab, on every page', async () => {
        renderAt('/media/1');

        await screen.findByRole('button', { name: 'Music' });
        expect(sidebar()).toHaveAttribute('data-collapsed', 'false');
        expect(screen.queryByRole('button', { name: 'Pin sidebar' })).toBeNull();
        cleanup();

        localStorage.setItem(StorageKeys.sidebarCollapsed, 'true');
        renderAt('/');

        await screen.findByRole('button', { name: 'Music' });
        expect(sidebar()).toHaveAttribute('data-collapsed', 'true');
    });

    it('is open on Home and folded on every other page in an installed app', async () => {
        installedApp();

        renderAt('/');
        await screen.findByRole('button', { name: 'Music' });
        expect(sidebar()).toHaveAttribute('data-collapsed', 'false');
        cleanup();

        renderAt('/media/1');
        await screen.findByRole('button', { name: 'Music' });
        expect(sidebar()).toHaveAttribute('data-collapsed', 'true');
        cleanup();

        renderAt('/server/second');
        await screen.findByRole('button', { name: 'Music' });
        expect(sidebar()).toHaveAttribute('data-collapsed', 'false');
    });

    it('keeps the viewer\'s own fold while pinned', async () => {
        installedApp();
        localStorage.setItem(StorageKeys.sidebarPinned, 'true');

        renderAt('/media/1');

        await screen.findByRole('button', { name: 'Music' });
        expect(sidebar()).toHaveAttribute('data-collapsed', 'false');
        expect(screen.getByRole('button', { name: 'Pin sidebar' })).toHaveAttribute('aria-pressed', 'true');
    });

    it('opens the sidebar when pinned from its rail, and remembers the pin', async () => {
        installedApp();
        localStorage.setItem(StorageKeys.sidebarCollapsed, 'true');
        renderAt('/media/1');
        await screen.findByRole('button', { name: 'Music' });
        expect(sidebar()).toHaveAttribute('data-collapsed', 'true');

        fireEvent.click(screen.getByRole('button', { name: 'Pin sidebar' }));

        expect(sidebar()).toHaveAttribute('data-collapsed', 'false');
        expect(localStorage.getItem(StorageKeys.sidebarPinned)).toBe('true');
        expect(localStorage.getItem(StorageKeys.sidebarCollapsed)).toBe('false');
    });

    it('folds away again when unpinned away from Home', async () => {
        installedApp();
        localStorage.setItem(StorageKeys.sidebarPinned, 'true');
        renderAt('/media/1');
        await screen.findByRole('button', { name: 'Music' });

        fireEvent.click(screen.getByRole('button', { name: 'Pin sidebar' }));

        expect(sidebar()).toHaveAttribute('data-collapsed', 'true');
        expect(localStorage.getItem(StorageKeys.sidebarPinned)).toBe('false');
    });
});
