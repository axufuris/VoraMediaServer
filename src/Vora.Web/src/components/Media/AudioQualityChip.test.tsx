import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import AudioQualityChip from './AudioQualityChip';
import type { AudioQualityVM } from '../../api/Music/musicService';

const flac: AudioQualityVM = { format: 'FLAC', sampleRate: 44100, lossless: true, hiRes: false, label: 'Lossless · FLAC 44.1 kHz' };
const mp3: AudioQualityVM = { format: 'MP3', bitrate: 192, lossless: false, hiRes: false, label: 'MP3 · 192 kbps' };

describe('AudioQualityChip', () => {
    it('shows the quality it is given', () => {
        render(<AudioQualityChip quality={flac} />);

        expect(screen.getByText('Lossless · FLAC 44.1 kHz')).toHaveAttribute('title', 'Lossless audio');
    });

    it('names the source when the stream was converted', () => {
        render(<AudioQualityChip quality={mp3} convertedFrom={flac} />);

        expect(screen.getByText('MP3 · 192 kbps')).toHaveAttribute('title', 'Compressed audio, converted from Lossless · FLAC 44.1 kHz');
    });

    it('shows nothing without a quality', () => {
        const { container } = render(<AudioQualityChip quality={null} convertedFrom={flac} />);

        expect(container).toBeEmptyDOMElement();
    });
});
