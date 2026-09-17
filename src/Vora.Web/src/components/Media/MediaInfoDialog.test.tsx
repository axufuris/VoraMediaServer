import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import MediaInfoDialog from './MediaInfoDialog';
import type { MediaPart } from '../../api/Media/mediaService';

const remux: MediaPart = {
    id: 'part-1',
    filePath: '/movies1080/Man of War (2026)/Man of War (2026) {imdb-tt34584846} [Remux-1080p][DTS-HD MA 5.1][AVC]-Aisha.mkv',
    resolution: '1080p',
    container: 'mkv',
    versionName: 'Remux',
    durationSeconds: 6638,
    fileSizeBytes: 9_266_000_000,
    bitrateKbps: 11167,
    videoTracks: [{ id: 'v1', codec: 'hevc', profile: 'main 10', bitDepth: 10, bitrateKbps: 9759, isDefault: true }],
    audioTracks: [
        { id: 'a1', codec: 'dts', channels: 6, language: 'eng', title: 'DTS-HD MA 5.1', isDefault: true },
        { id: 'a2', codec: 'ac3', channels: 2, language: 'eng', title: 'Commentary' },
    ],
    subtitleTracks: [
        { id: 's1', codec: 'hdmv_pgs_subtitle', language: 'eng', isForced: true },
        { id: 's2', codec: 'subrip', language: 'eng', isExternal: true, isDownloaded: true },
    ],
};

const renderDialog = (parts: MediaPart[], placement: 'absolute' | 'fixed' = 'fixed') => {
    const onClose = vi.fn();
    render(<div data-vora-client=""><MediaInfoDialog parts={parts} placement={placement} onClose={onClose} /></div>);
    return onClose;
};

const statValue = (section: HTMLElement, label: string) =>
    within(section).getByText(label, { selector: 'dt' }).nextElementSibling?.textContent;

describe('media info dialog', () => {
    it('is a labelled dialog', () => {
        renderDialog([remux]);

        expect(screen.getByRole('dialog', { name: 'Media info' })).toBeInTheDocument();
    });

    it('shows the full file path', () => {
        renderDialog([remux]);

        expect(screen.getByText(remux.filePath)).toBeInTheDocument();
    });

    it('describes the part', () => {
        renderDialog([remux]);
        const part = screen.getByRole('heading', { name: 'Part' }).closest('section')!;

        expect(statValue(part, 'Duration')).toBe('1:50:38');
        expect(statValue(part, 'Size')).toBe('8.63 GB');
        expect(statValue(part, 'Bitrate')).toBe('11,167 kbps');
        expect(statValue(part, 'Container')).toBe('MKV');
        expect(statValue(part, 'Version')).toBe('Remux');
    });

    // The path heads its own section; repeating the file name as a row beside
    // the stats only wrapped over several lines.
    it('does not repeat the file name as a stat', () => {
        renderDialog([remux]);
        const part = screen.getByRole('heading', { name: 'Part' }).closest('section')!;

        expect(within(part).queryByText('File', { selector: 'dt' })).toBeNull();
    });

    it('describes the video stream', () => {
        renderDialog([remux]);
        const video = screen.getByRole('heading', { name: 'Video' }).closest('section')!;

        expect(statValue(video, 'Codec')).toBe('HEVC');
        expect(statValue(video, 'Bit depth')).toBe('10-bit');
        expect(statValue(video, 'Bitrate')).toBe('9,759 kbps');
    });

    it('lists every audio stream with its layout and language', () => {
        renderDialog([remux]);
        const audio = screen.getByRole('heading', { name: 'Audio · 2' }).closest('section')!;

        expect(within(audio).getByText('5.1')).toBeInTheDocument();
        expect(within(audio).getByText('Stereo')).toBeInTheDocument();
        expect(within(audio).getAllByText('English (eng)')).toHaveLength(2);
        expect(within(audio).getByText('Commentary')).toBeInTheDocument();
    });

    it('says where each subtitle comes from', () => {
        renderDialog([remux]);
        const subtitles = screen.getByRole('heading', { name: 'Subtitles · 2' }).closest('section')!;

        expect(within(subtitles).getByText('Embedded')).toBeInTheDocument();
        expect(within(subtitles).getByText('Downloaded')).toBeInTheDocument();
    });

    // A blank row reads as "unknown" data rather than missing data.
    it('leaves out facts the file does not have', () => {
        renderDialog([{ id: 'bare', filePath: '/tv/Show/S01E01.mkv', videoTracks: [{ id: 'v', codec: 'h264' }] }]);
        const part = screen.getByRole('heading', { name: 'Part' }).closest('section')!;

        expect(within(part).queryByText('Duration')).toBeNull();
        expect(within(part).queryByText('Size')).toBeNull();
        expect(within(screen.getByRole('heading', { name: 'Video' }).closest('section')!).queryByText('HDR')).toBeNull();
    });

    it('says None for a part with no audio or subtitles', () => {
        renderDialog([{ id: 'bare', filePath: '/tv/Show/S01E01.mkv' }]);

        expect(screen.getAllByText('None').length).toBeGreaterThanOrEqual(3);
    });

    const fourK: MediaPart = { ...remux, id: 'part-2', filePath: '/movies4k/Man of War (2026)/Man of War (2026) [2160p].mkv', resolution: '2160p' };

    it('numbers the files when an item has more than one', () => {
        renderDialog([remux, fourK]);

        expect(screen.getByRole('heading', { name: 'File 1 of 2' })).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'File 2 of 2' })).toBeInTheDocument();
    });

    // Each file's path belongs to that file's details, not to a list of paths
    // at the top that the reader has to match up with the sections below.
    it('puts each file path directly above its own details', () => {
        renderDialog([remux, fourK]);

        const second = screen.getByRole('article', { name: 'File 2 of 2' });
        const path = within(second).getByText(fourK.filePath);
        const resolution = within(second).getByText('Resolution', { selector: 'dt' });

        expect(statValue(second, 'Resolution')).toBe('4K');
        expect(within(second).queryByText(remux.filePath)).toBeNull();
        expect(path.compareDocumentPosition(resolution) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('closes from the close button', () => {
        const onClose = renderDialog([remux]);

        fireEvent.click(screen.getByRole('button', { name: 'Close' }));

        expect(onClose).toHaveBeenCalledOnce();
    });

    it('closes on Escape and focuses close when it opens', () => {
        const onClose = renderDialog([remux]);

        expect(screen.getByRole('button', { name: 'Close' })).toHaveFocus();
        fireEvent.keyDown(window, { key: 'Escape' });

        expect(onClose).toHaveBeenCalledOnce();
    });

    it('closes when clicking outside the card but not inside it', () => {
        const onClose = renderDialog([remux]);

        fireEvent.click(screen.getByRole('dialog'));
        expect(onClose).not.toHaveBeenCalled();

        fireEvent.click(screen.getByRole('dialog').parentElement!);
        expect(onClose).toHaveBeenCalledOnce();
    });

    // Over the header on the details page; confined to the video in the player.
    it.each([
        ['fixed', 'z-[200]'],
        ['absolute', 'z-50'],
    ] as const)('sits %s', (placement, zClass) => {
        renderDialog([remux], placement);

        const overlay = screen.getByRole('dialog').parentElement!;
        expect(overlay).toHaveClass(placement);
        expect(overlay).toHaveClass(zClass);
    });
});
