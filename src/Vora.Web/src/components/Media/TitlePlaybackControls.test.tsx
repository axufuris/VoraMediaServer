import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import TitlePlaybackControls, { TitlePlaybackAdvanced } from './TitlePlaybackControls';

const props = {
    videoOptions: [{ value: 'video', label: '1080p H264' }], audioOptions: [{ value: 'six', label: 'AAC 5.1' }, { value: 'two', label: 'AAC 2.0' }], subtitleOptions: [{ value: 'none', label: 'Off' }, { value: 'english', label: 'English' }],
    videoId: 'video', audioId: 'six', subtitleId: 'none',
    onVideoChange: vi.fn(), onAudioChange: vi.fn(), onSubtitleChange: vi.fn(),
};

describe('compact title playback controls', () => {
    it('shows a read-only quality pill for one file and no visible field headings', () => {
        render(<TitlePlaybackControls {...props} />);
        expect(screen.getByRole('combobox', { name: 'Video', exact: true })).toBeDisabled();
        expect(screen.getByRole('combobox', { name: 'Video', exact: true })).toHaveValue('video');
        expect(screen.getByRole('combobox', { name: 'Audio', exact: true })).toBeEnabled();
        expect(screen.queryByText('Video', { exact: true })).not.toBeInTheDocument();
        expect(screen.queryByText('Playback preferences', { exact: true })).not.toBeInTheDocument();
    });
    it('enables version selection only when multiple file choices are available', async () => {
        const change = vi.fn();
        render(<TitlePlaybackControls {...props} videoOptions={[...props.videoOptions, { value: 'other-file', label: '4K HEVC' }]} onVideoChange={change} />);
        await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Video', exact: true }), 'other-file');
        expect(change).toHaveBeenCalledWith('other-file');
    });
    it('selects an available stereo track and explicit subtitle off', async () => {
        render(<TitlePlaybackControls {...props} />);
        await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Audio', exact: true }), 'two');
        await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Subtitles' }), 'none');
        expect(props.onAudioChange).toHaveBeenCalledWith('two');
        expect(props.onSubtitleChange).toHaveBeenCalledWith('none');
    });
    it('shows a read-only audio pill with one track and subtitle off with no tracks', () => {
        render(<TitlePlaybackControls {...props} audioOptions={[props.audioOptions[0]]} subtitleOptions={[props.subtitleOptions[0]]} />);
        expect(screen.getByRole('combobox', { name: 'Audio', exact: true })).toBeDisabled();
        expect(screen.getByRole('combobox', { name: 'Subtitles' })).toBeDisabled();
    });
    it('keeps surround output overrides in the advanced controls', async () => {
        const change = vi.fn();
        render(<TitlePlaybackAdvanced preferences={{ maxAudioChannels: 6 }} onPreferencesChange={change} onReset={vi.fn()} />);
        expect(screen.getByRole('combobox', { name: 'Audio output' })).toHaveValue('6');
        await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Audio output' }), 'global');
        expect(change).toHaveBeenCalledWith({ maxAudioChannels: undefined });
    });
    it('distinguishes original quality from inheriting a global limit', async () => {
        const change = vi.fn();
        render(<TitlePlaybackAdvanced preferences={{}} onPreferencesChange={change} onReset={vi.fn()} />);
        await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Video quality' }), '0');
        expect(change).toHaveBeenCalledWith({ maxResolution: 0 });
    });
    it('resets saved overrides from the advanced controls', async () => {
        const reset = vi.fn();
        render(<TitlePlaybackAdvanced preferences={{ maxAudioChannels: 6 }} onPreferencesChange={vi.fn()} onReset={reset} />);
        await userEvent.click(screen.getByRole('button', { name: 'Use global defaults' }));
        expect(reset).toHaveBeenCalledOnce();
    });
});
