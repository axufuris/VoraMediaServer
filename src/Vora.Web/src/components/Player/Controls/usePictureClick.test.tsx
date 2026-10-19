import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { useState } from 'react';
import { usePictureClick } from './usePictureClick';
import PlayerKeyIndicator from './PlayerKeyIndicator';

class TestPointerEvent extends MouseEvent {
    readonly pointerType: string;

    constructor(type: string, init: PointerEventInit = {}) {
        super(type, init);
        this.pointerType = init.pointerType ?? '';
    }
}

function Harness({ onPlayPause }: { onPlayPause: () => void }) {
    const [playing, setPlaying] = useState(true);
    const picture = usePictureClick({ isPlaying: playing, onPlayPause: () => { setPlaying(was => !was); onPlayPause(); } });
    return (
        <div>
            <div data-testid="picture" onPointerDown={picture.onPointerDown} onClick={picture.onClick}>
                <button type="button">Subtitles</button>
                <div role="slider" aria-label="Playback position" aria-valuenow={0} tabIndex={0} />
            </div>
            <PlayerKeyIndicator indicator={picture.indicator} />
        </div>
    );
}

const click = (target: HTMLElement, pointerType: string, button = 0) => {
    fireEvent.pointerDown(target, { pointerType, button });
    fireEvent.click(target, { button });
};

describe('usePictureClick', () => {
    beforeEach(() => {
        vi.useFakeTimers();
        vi.stubGlobal('PointerEvent', TestPointerEvent);
    });

    afterEach(() => {
        vi.useRealTimers();
        vi.unstubAllGlobals();
    });

    it('pauses and plays on a mouse click on the picture, and says so', () => {
        const onPlayPause = vi.fn();
        render(<Harness onPlayPause={onPlayPause} />);

        click(screen.getByTestId('picture'), 'mouse');
        expect(onPlayPause).toHaveBeenCalledTimes(1);
        expect(screen.getByRole('status')).toHaveTextContent('Paused');

        click(screen.getByTestId('picture'), 'mouse');
        expect(onPlayPause).toHaveBeenCalledTimes(2);
        expect(screen.getByRole('status')).toHaveTextContent('Playing');

        act(() => { vi.advanceTimersByTime(1000); });
        expect(screen.getByRole('status')).not.toHaveTextContent('Playing');
    });

    it('leaves a tap on a touch screen to the controls', () => {
        const onPlayPause = vi.fn();
        render(<Harness onPlayPause={onPlayPause} />);

        click(screen.getByTestId('picture'), 'touch');

        expect(onPlayPause).not.toHaveBeenCalled();
    });

    it('ignores a click on a control, a right click, and a drag released over the picture', () => {
        const onPlayPause = vi.fn();
        render(<Harness onPlayPause={onPlayPause} />);

        click(screen.getByRole('button', { name: 'Subtitles' }), 'mouse');
        click(screen.getByTestId('picture'), 'mouse', 2);
        fireEvent.pointerDown(screen.getByRole('slider', { name: 'Playback position' }), { pointerType: 'mouse', button: 0 });
        fireEvent.click(screen.getByTestId('picture'));

        expect(onPlayPause).not.toHaveBeenCalled();
    });
});
