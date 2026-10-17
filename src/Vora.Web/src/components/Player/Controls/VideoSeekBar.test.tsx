import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import VideoSeekBar from './VideoSeekBar';

class TestPointerEvent extends MouseEvent {
    readonly pointerId: number;
    readonly pointerType: string;

    constructor(type: string, init: PointerEventInit = {}) {
        super(type, init);
        this.pointerId = init.pointerId ?? 0;
        this.pointerType = init.pointerType ?? '';
    }
}

const track = { left: 100, width: 400, top: 500, height: 6, right: 500, bottom: 506, x: 100, y: 500, toJSON: () => ({}) } as DOMRect;
const at = (percent: number) => track.left + track.width * percent;

function renderBar(currentTime = 0, duration = 1000) {
    const onSeek = vi.fn();
    const onPointAt = vi.fn();
    render(<VideoSeekBar variant="full" currentTime={currentTime} duration={duration} onSeek={onSeek} onPointAt={onPointAt} />);
    return { bar: screen.getByRole('slider', { name: 'Playback position' }), onSeek, onPointAt };
}

const fillWidth = () => screen.getByTestId('seek-fill').style.width;

describe('VideoSeekBar', () => {
    beforeEach(() => {
        vi.stubGlobal('PointerEvent', TestPointerEvent);
        vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue(track);
    });
    afterEach(() => {
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
    });

    it('follows a drag and seeks once, where the drag is let go', () => {
        const { bar, onSeek } = renderBar(100);

        fireEvent.pointerDown(bar, { clientX: at(0.25), pointerId: 1, pointerType: 'mouse', button: 0 });
        fireEvent.pointerMove(bar, { clientX: at(0.5), pointerId: 1, pointerType: 'mouse' });
        fireEvent.pointerMove(bar, { clientX: at(0.75), pointerId: 1, pointerType: 'mouse' });

        expect(fillWidth()).toBe('75%');
        expect(onSeek).not.toHaveBeenCalled();

        fireEvent.pointerUp(bar, { clientX: at(0.75), pointerId: 1, pointerType: 'mouse' });

        expect(onSeek).toHaveBeenCalledTimes(1);
        expect(onSeek).toHaveBeenCalledWith(750);
        expect(fillWidth()).toBe('10%');
    });

    it('seeks to where a click lands', () => {
        const { bar, onSeek } = renderBar();

        fireEvent.pointerDown(bar, { clientX: at(0.4), pointerId: 1, pointerType: 'mouse', button: 0 });
        fireEvent.pointerUp(bar, { clientX: at(0.4), pointerId: 1, pointerType: 'mouse' });

        expect(onSeek).toHaveBeenCalledWith(400);
    });

    it('drags with a finger and takes the preview away when the finger lifts', () => {
        const { bar, onSeek, onPointAt } = renderBar();

        fireEvent.pointerDown(bar, { clientX: at(0.2), pointerId: 7, pointerType: 'touch' });
        fireEvent.pointerMove(bar, { clientX: at(0.6), pointerId: 7, pointerType: 'touch' });

        expect(onPointAt).toHaveBeenLastCalledWith(0.6, track);

        fireEvent.pointerUp(bar, { clientX: at(0.6), pointerId: 7, pointerType: 'touch' });

        expect(onSeek).toHaveBeenCalledWith(600);
        expect(onPointAt).toHaveBeenLastCalledWith(null, null);
    });

    it('keeps the previous position when the browser takes the touch away mid-drag', () => {
        const { bar, onSeek, onPointAt } = renderBar(500);

        fireEvent.pointerDown(bar, { clientX: at(0.9), pointerId: 7, pointerType: 'touch' });
        fireEvent.pointerCancel(bar, { clientX: at(0.9), pointerId: 7, pointerType: 'touch' });

        expect(onSeek).not.toHaveBeenCalled();
        expect(fillWidth()).toBe('50%');
        expect(onPointAt).toHaveBeenLastCalledWith(null, null);
    });

    it('stays put when the pointer is dragged past either end', () => {
        const { bar, onSeek } = renderBar();

        fireEvent.pointerDown(bar, { clientX: at(0.5), pointerId: 1, pointerType: 'mouse', button: 0 });
        fireEvent.pointerUp(bar, { clientX: track.left + track.width + 300, pointerId: 1, pointerType: 'mouse' });

        expect(onSeek).toHaveBeenCalledWith(1000);
    });

    it('shows the preview under a hovering mouse without seeking, and hides it when the mouse leaves', () => {
        const { bar, onSeek, onPointAt } = renderBar();

        fireEvent.pointerMove(bar, { clientX: at(0.3), pointerId: 1, pointerType: 'mouse' });
        expect(onPointAt).toHaveBeenLastCalledWith(0.3, track);

        fireEvent.pointerLeave(bar, { pointerId: 1, pointerType: 'mouse' });
        expect(onPointAt).toHaveBeenLastCalledWith(null, null);
        expect(onSeek).not.toHaveBeenCalled();
    });

    it('ignores a right click', () => {
        const { bar, onSeek } = renderBar();

        fireEvent.pointerDown(bar, { clientX: at(0.4), pointerId: 1, pointerType: 'mouse', button: 2 });
        fireEvent.pointerUp(bar, { clientX: at(0.4), pointerId: 1, pointerType: 'mouse', button: 2 });

        expect(onSeek).not.toHaveBeenCalled();
    });

    it('does nothing until the length is known', () => {
        const { bar, onSeek } = renderBar(0, Number.NaN);

        fireEvent.pointerDown(bar, { clientX: at(0.4), pointerId: 1, pointerType: 'mouse', button: 0 });
        fireEvent.pointerUp(bar, { clientX: at(0.4), pointerId: 1, pointerType: 'mouse' });

        expect(onSeek).not.toHaveBeenCalled();
        expect(bar).toHaveAttribute('aria-valuemax', '0');
    });

    it('tells a screen reader where playback is', () => {
        const { bar } = renderBar(754, 2700);

        expect(bar).toHaveAttribute('aria-valuenow', '754');
        expect(bar).toHaveAttribute('aria-valuemax', '2700');
        expect(bar).toHaveAttribute('aria-valuetext', '12:34 of 45:00');
    });
});
