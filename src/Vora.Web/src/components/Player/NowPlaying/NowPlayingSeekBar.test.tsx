import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { NowPlayingSeekBar } from './NowPlayingControls';

describe('NowPlayingSeekBar', () => {
    it('spans the track length', () => {
        render(<NowPlayingSeekBar currentTime={110} duration={245} onSeek={() => { }} />);

        expect(screen.getByLabelText('Playback position')).toHaveAttribute('max', '245');
        expect(screen.getByText('4:05')).toBeInTheDocument();
    });

    it('does not fill up when the length is unknown', () => {
        render(<NowPlayingSeekBar currentTime={110} duration={Infinity} onSeek={() => { }} />);

        expect(screen.getByLabelText('Playback position')).toHaveAttribute('max', '0');
    });
});
