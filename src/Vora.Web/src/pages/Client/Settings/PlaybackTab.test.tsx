import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { ReactElement } from 'react';
import PlaybackTab from './PlaybackTab';
import { DialogProvider } from '../../../dialogs';
import { profileDeviceSettingsService } from '../../../api/Users/profileDeviceSettingsService';
import { StorageKeys } from '../../../utils/storageKeys';
import { announcePlaybackPrefsChanged } from '../../../utils/playbackPrefsEvents';

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
    expect(profileDeviceSettingsService.saveClientSettings).toHaveBeenCalledWith('profile', 'device', { playbackPrefs: saved }, 'server');
});

it('offers the arrow-key steps, 10 seconds and 5% until changed', () => {
    renderTab(<PlaybackTab activeProfileId="profile" onSaved={vi.fn()} />);

    expect(screen.getByLabelText('Left and Right skip')).toHaveValue('10');
    expect(screen.getByLabelText('Up and Down change volume by')).toHaveValue('5');
});

it('saves the arrow-key steps with the other playback settings and keeps the existing ones', async () => {
    localStorage.setItem(StorageKeys.deviceId, 'device');
    localStorage.setItem('playback_prefs_profile_device', JSON.stringify({ bitrate: 20, maxResolution: 1080, maxAudioChannels: 2 }));
    const onSaved = vi.fn();
    renderTab(<PlaybackTab activeProfileId="profile" onSaved={onSaved} />);
    await waitFor(() => expect(screen.getByLabelText('Audio output')).toHaveValue('2'));

    fireEvent.change(screen.getByLabelText('Left and Right skip'), { target: { value: '30' } });
    fireEvent.change(screen.getByLabelText('Up and Down change volume by'), { target: { value: '10' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save playback settings' }));
    await waitFor(() => expect(onSaved).toHaveBeenCalledOnce());

    const saved = JSON.parse(localStorage.getItem('playback_prefs_profile_device') ?? '{}');
    expect(saved).toEqual({ bitrate: 20, maxResolution: 1080, maxAudioChannels: 2, seekStepSeconds: 30, volumeStepPercent: 10 });
});

it('shows the saved arrow-key steps when the tab opens', async () => {
    localStorage.setItem(StorageKeys.deviceId, 'device');
    localStorage.setItem('playback_prefs_profile_device', JSON.stringify({ seekStepSeconds: 5, volumeStepPercent: 2 }));
    renderTab(<PlaybackTab activeProfileId="profile" onSaved={vi.fn()} />);

    await waitFor(() => expect(screen.getByLabelText('Left and Right skip')).toHaveValue('5'));
    expect(screen.getByLabelText('Up and Down change volume by')).toHaveValue('2');
});

it('sends only the playback settings, so a save never carries the Live TV preferences', async () => {
    localStorage.setItem(StorageKeys.deviceId, 'device');
    renderTab(<PlaybackTab activeProfileId="profile" serverId="server" onSaved={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Save playback settings' }));

    await waitFor(() => expect(profileDeviceSettingsService.saveClientSettings).toHaveBeenCalled());
    const blobs = vi.mocked(profileDeviceSettingsService.saveClientSettings).mock.calls[0][2];
    expect(Object.keys(blobs)).toEqual(['playbackPrefs']);
});

it('says so when the server cannot be reached, and keeps what this device had', async () => {
    localStorage.setItem(StorageKeys.deviceId, 'device');
    localStorage.setItem('playback_prefs_profile_device', JSON.stringify({ bitrate: 20, maxResolution: 1080, maxAudioChannels: 2, seekStepSeconds: 10, volumeStepPercent: 5 }));
    vi.mocked(profileDeviceSettingsService.saveClientSettings).mockRejectedValueOnce(new Error('offline'));
    const onSaved = vi.fn();
    renderTab(<PlaybackTab activeProfileId="profile" serverId="server" onSaved={onSaved} />);
    await waitFor(() => expect(screen.getByLabelText('Audio output')).toHaveValue('2'));

    fireEvent.change(screen.getByLabelText('Audio output'), { target: { value: '8' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save playback settings' }));

    expect(await screen.findByText('Could not save your playback settings: offline')).toBeInTheDocument();
    expect(onSaved).not.toHaveBeenCalled();
    expect(JSON.parse(localStorage.getItem('playback_prefs_profile_device') ?? '{}').maxAudioChannels).toBe(2);
});

it('follows the settings when the server changes them, until something has been edited', async () => {
    localStorage.setItem(StorageKeys.deviceId, 'device');
    localStorage.setItem('playback_prefs_profile_device', JSON.stringify({ bitrate: 0, maxResolution: 0, maxAudioChannels: 0, seekStepSeconds: 10, volumeStepPercent: 5 }));
    renderTab(<PlaybackTab activeProfileId="profile" onSaved={vi.fn()} />);

    localStorage.setItem('playback_prefs_profile_device', JSON.stringify({ bitrate: 0, maxResolution: 0, maxAudioChannels: 0, seekStepSeconds: 30, volumeStepPercent: 5 }));
    announcePlaybackPrefsChanged();
    await waitFor(() => expect(screen.getByLabelText('Left and Right skip')).toHaveValue('30'));

    fireEvent.change(screen.getByLabelText('Up and Down change volume by'), { target: { value: '10' } });
    localStorage.setItem('playback_prefs_profile_device', JSON.stringify({ bitrate: 0, maxResolution: 0, maxAudioChannels: 0, seekStepSeconds: 5, volumeStepPercent: 2 }));
    announcePlaybackPrefsChanged();
    await Promise.resolve();
    expect(screen.getByLabelText('Left and Right skip')).toHaveValue('30');
    expect(screen.getByLabelText('Up and Down change volume by')).toHaveValue('10');
});

it('refreshes automatic detection after an output-device change', async () => {
    renderTab(<PlaybackTab activeProfileId="profile" onSaved={vi.fn()} />);
    outputChannels = 2;
    audioDevices.dispatchEvent(new Event('devicechange'));
    await waitFor(() => expect(screen.getByRole('option', { name: 'Automatic (2 channels detected)' })).toBeInTheDocument());
});
