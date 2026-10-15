import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { ReactElement } from 'react';
import PlaybackTab from './PlaybackTab';
import { DialogProvider } from '../../../dialogs';
import { profileDeviceSettingsService } from '../../../api/Users/profileDeviceSettingsService';
import { StorageKeys } from '../../../utils/storageKeys';

vi.mock('../../../api/Users/profileDeviceSettingsService', () => ({
    profileDeviceSettingsService: { saveClientSettings: vi.fn().mockResolvedValue(undefined) },
}));

vi.mock('../../../api/Users/profileService', () => ({
    profileService: {
        getMyTrackPreferences: vi.fn().mockResolvedValue({ preferHearingImpairedSubtitles: false, serverLanguage: 'eng', serverSubtitleMode: 'Smart' }),
    },
}));

const renderTab = (tab: ReactElement) => render(<DialogProvider>{tab}</DialogProvider>);

let outputChannels = 6;
const audioDevices = new EventTarget();

beforeEach(() => {
    localStorage.clear();
    vi.clearAllMocks();
    outputChannels = 6;
    vi.stubGlobal('AudioContext', class {
        destination = { maxChannelCount: outputChannels };
        close = () => Promise.resolve();
    });
    Object.defineProperty(navigator, 'mediaDevices', { configurable: true, value: audioDevices });
});

afterEach(() => {
    vi.unstubAllGlobals();
    Reflect.deleteProperty(navigator, 'mediaDevices');
});

it('shows the automatically detected speaker layout', () => {
    renderTab(<PlaybackTab activeProfileId="profile" onSaved={vi.fn()} />);
    expect(screen.getByRole('option', { name: 'Automatic (6 channels detected)' })).toBeInTheDocument();
    expect(screen.getByLabelText('Audio output')).toHaveValue('0');
});

it('saves a 5.1 override for this profile and device', async () => {
    localStorage.setItem(StorageKeys.deviceId, 'device');
    const onSaved = vi.fn();
    renderTab(<PlaybackTab activeProfileId="profile" serverId="server" onSaved={onSaved} />);
    fireEvent.change(screen.getByLabelText('Audio output'), { target: { value: '6' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save playback settings' }));
    await waitFor(() => expect(onSaved).toHaveBeenCalledOnce());
    const saved = localStorage.getItem('playback_prefs_profile_device');
    expect(saved && JSON.parse(saved).maxAudioChannels).toBe(6);
    expect(profileDeviceSettingsService.saveClientSettings).toHaveBeenCalledWith('profile', 'device', saved, '{}', 'server');
});

it('refreshes automatic detection after an output-device change', async () => {
    renderTab(<PlaybackTab activeProfileId="profile" onSaved={vi.fn()} />);
    outputChannels = 2;
    audioDevices.dispatchEvent(new Event('devicechange'));
    await waitFor(() => expect(screen.getByRole('option', { name: 'Automatic (2 channels detected)' })).toBeInTheDocument());
});
