import { describe, expect, it } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import { useRef } from 'react';
import { usePictureLayout } from './usePictureLayout';
import { cropOf, NoCrop, type PictureCrop, type PictureFit, type PictureLayout } from '../../../utils/pictureLayout';

const letterbox = cropOf({ width: 1920, height: 1080, cropTop: 60, cropRight: 0, cropBottom: 60, cropLeft: 0 });

const sized = (element: HTMLElement | null, width: number, height: number) => {
    if (!element) return;
    Object.defineProperty(element, 'clientWidth', { configurable: true, value: width });
    Object.defineProperty(element, 'clientHeight', { configurable: true, value: height });
};

const playing = (video: HTMLVideoElement | null, width: number, height: number) => {
    if (!video) return;
    Object.defineProperty(video, 'videoWidth', { configurable: true, value: width });
    Object.defineProperty(video, 'videoHeight', { configurable: true, value: height });
};

function Harness({ crop, fit, enabled = true }: { crop: PictureCrop; fit: PictureFit; enabled?: boolean }) {
    const containerRef = useRef<HTMLDivElement>(null);
    const videoRef = useRef<HTMLVideoElement>(null);
    const framing = usePictureLayout({ containerRef, videoRef, videoKey: 'session', crop, fit, enabled });
    return (
        <div ref={element => { sized(element, 1600, 900); containerRef.current = element; }}>
            <video data-testid="video" ref={element => { playing(element, 1920, 1080); videoRef.current = element; }} />
            <pre data-testid="layout">{JSON.stringify(framing.layout)}</pre>
            <span data-testid="can-fill">{String(framing.canFill)}</span>
        </div>
    );
}

const layout = (): PictureLayout | null => JSON.parse(screen.getByTestId('layout').textContent ?? 'null');

describe('laying out the playing picture', () => {
    it('leaves a frame with no bars to the browser while it fits', () => {
        render(<Harness crop={NoCrop} fit="fit" />);

        expect(layout()).toBeNull();
        expect(screen.getByTestId('can-fill').textContent).toBe('false');
    });

    it('shows a letterboxed picture whole in a window shaped like its file, and offers to fill it', () => {
        render(<Harness crop={letterbox} fit="fit" />);

        expect(layout()).toEqual({ width: 1600, height: 900, left: 0, top: 0 });
        expect(screen.getByTestId('can-fill').textContent).toBe('true');
    });

    it('fills the window when asked, trimming the sides', () => {
        render(<Harness crop={letterbox} fit="fill" />);

        expect(layout()?.height).toBeCloseTo(900 * 1080 / 960);
        expect(layout()?.left).toBeCloseTo((1600 - 1800) / 2);
    });

    it('follows the window and the video as they change size', () => {
        const { container } = render(<Harness crop={letterbox} fit="fit" />);

        act(() => {
            sized(container.firstElementChild as HTMLElement, 2560, 1080);
            window.dispatchEvent(new Event('resize'));
        });
        expect(layout()?.height).toBeCloseTo(1080 * 1080 / 960);

        const video = screen.getByTestId('video') as HTMLVideoElement;
        act(() => {
            playing(video, 1280, 720);
            video.dispatchEvent(new Event('resize'));
        });
        expect(layout()?.height).toBeCloseTo(1080 * 720 / 640);
    });

    it('lays out nothing while the player is minimized or ending', () => {
        render(<Harness crop={letterbox} fit="fill" enabled={false} />);

        expect(layout()).toBeNull();
        expect(screen.getByTestId('can-fill').textContent).toBe('false');
    });
});
