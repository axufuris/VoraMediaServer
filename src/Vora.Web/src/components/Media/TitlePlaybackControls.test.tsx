import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import TitlePlaybackControls from './TitlePlaybackControls';

const props = {
    videoOptions: [{ value: 'video', label: '1080p H264' }], audioOptions: [{ value: 'six', label: 'AAC 6ch' }, { value: 'two', label: 'AAC 2ch' }], subtitleOptions: [{ value: 'none', label: 'Off' }, { value: 'english', label: 'English' }],
    videoId: 'video', audioId: 'six', subtitleId: 'none', preferences: {}, isShow: false,
    onVideoChange: vi.fn(), onAudioChange: vi.fn(), onSubtitleChange: vi.fn(), onPreferencesChange: vi.fn(), onReset: vi.fn(),
};

describe('visible title playback controls', () => {
    it('shows accessible video, audio and subtitle selectors even for one video track', () => {
        render(<TitlePlaybackControls {...props} />);
        expect(screen.getByRole('combobox', { name: 'Video', exact: true })).toHaveValue('video');
        expect(screen.getByRole('combobox', { name: 'Audio', exact: true })).toHaveValue('six');
        expect(screen.getByRole('combobox', { name: 'Subtitles' })).toHaveValue('none');
    });
    it('selects a stereo track and explicit subtitle off', async () => {
        render(<TitlePlaybackControls {...props} />);
        await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Audio', exact: true }), 'two');
        await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Subtitles' }), 'none');
        expect(props.onAudioChange).toHaveBeenCalledWith('two');
        expect(props.onSubtitleChange).toHaveBeenCalledWith('none');
    });
    it('allows surround to override a global output and returns it to global', async () => {
        const change = vi.fn();
        render(<TitlePlaybackControls {...props} preferences={{ maxAudioChannels: 6 }} onPreferencesChange={change} />);
        expect(screen.getByRole('combobox', { name: 'Audio output' })).toHaveValue('6');
        await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Audio output' }), 'global');
        expect(change).toHaveBeenCalledWith({ maxAudioChannels: undefined });
    });
    it('distinguishes original quality from inheriting a global limit', async () => {
        const change = vi.fn();
        render(<TitlePlaybackControls {...props} onPreferencesChange={change} />);
        await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Video quality' }), '0');
        expect(change).toHaveBeenCalledWith({ maxResolution: 0 });
    });
    it('explains show scope and resets the saved override', async () => {
        const reset = vi.fn();
        render(<TitlePlaybackControls {...props} isShow preferences={{ maxAudioChannels: 6 }} onReset={reset} />);
        expect(screen.getByText(/including its episodes/)).toBeInTheDocument();
        await userEvent.click(screen.getByRole('button', { name: 'Use global defaults' }));
        expect(reset).toHaveBeenCalledOnce();
    });
});
