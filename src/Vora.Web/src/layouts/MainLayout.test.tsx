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
    getPlaybackPrefs: vi.fn(),
    playbackCapabilities: vi.fn(),
}));

vi.mock('../api/Users/profileDeviceSettingsService', () => ({
    profileDeviceSettingsService: { getNavPrefs: mocks.getNavPrefs, saveNavPrefs: mocks.saveNavPrefs, getPlaybackPrefs: mocks.getPlaybackPrefs },
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
    default: ({ navItems, onTogglePin }: { navItems: NavItem[]; onTogglePin: (id: string, serverId?: string) => void }) => (
        <nav>{navItems.map(item => <button key={`${item.serverId ?? ''}-${item.id}`} type="button" onClick={() => onTogglePin(item.id, item.serverId)}>{item.title}</button>)}</nav>
    ),
}));

const tokenFor = (profileId: string) => `header.${btoa(JSON.stringify({ sub: profileId }))}.signature`;

const renderAt = (path: string) => render(
    <MemoryRouter initialEntries={[path]}>
        <Routes>
            <Route path="/" element={<MainLayout />}>
                <Route index element={<div />} />
                <Route path="server/:serverId" element={<div />} />
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
    mocks.getPlaybackPrefs.mockResolvedValue('{"bitrate":8}');
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

        await waitFor(() => expect(mocks.getPlaybackPrefs).toHaveBeenCalled());
        expect(mocks.getNavPrefs).toHaveBeenCalledWith('profile-2', 'device-1', 'second');
        expect(mocks.getPlaybackPrefs).toHaveBeenCalledWith('profile-2', 'device-1', 'second');
        await waitFor(() => expect(localStorage.getItem('playback_prefs_profile-2_device-1')).toBe('{"bitrate":8}'));
        expect(localStorage.getItem('playback_prefs_profile-1_device-1')).toBeNull();

        fireEvent.click(await screen.findByRole('button', { name: 'Music' }));

        expect(mocks.saveNavPrefs).toHaveBeenCalledWith('profile-2', 'device-1', expect.any(String), 'second');
    });

    it('keeps the primary profile on the primary server', async () => {
        renderAt('/');

        await waitFor(() => expect(mocks.getPlaybackPrefs).toHaveBeenCalled());
        expect(mocks.getNavPrefs).toHaveBeenCalledWith('profile-1', 'device-1', undefined);
        expect(mocks.getPlaybackPrefs).toHaveBeenCalledWith('profile-1', 'device-1', undefined);

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
