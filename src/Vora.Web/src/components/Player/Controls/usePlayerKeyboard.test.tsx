import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import { useState } from 'react';
import { usePlayerKeyboard } from './usePlayerKeyboard';
import PlayerKeyIndicator from './PlayerKeyIndicator';
import { StorageKeys } from '../../../utils/storageKeys';

interface HarnessProps {
    enabled?: boolean;
    canSeek?: boolean;
    plainArrows?: boolean;
    startVolume?: number;
    startPlaying?: boolean;
    onPlayPause?: () => void;
    onSkipForward?: (seconds: number) => void;
    onSkipBackward?: (seconds: number) => void;
    onVolume?: (volume: number) => void;
}

function Harness({ enabled = true, canSeek = true, plainArrows, startVolume = 0.5, startPlaying = true, onPlayPause, onSkipForward = vi.fn(), onSkipBackward = vi.fn(), onVolume }: HarnessProps) {
    const [volume, setVolume] = useState(startVolume);
    const [playing, setPlaying] = useState(startPlaying);
    const indicator = usePlayerKeyboard({
        enabled,
        canSeek,
        plainArrows,
        volume,
        isPlaying: playing,
        onPlayPause: () => { setPlaying(was => !was); onPlayPause?.(); },
        onSkipForward,
        onSkipBackward,
        onVolume: level => { setVolume(level); onVolume?.(level); },
    });
    return (
        <div>
            <input aria-label="Search" />
            <input aria-label="Volume" type="range" />
            <input aria-label="Remember" type="checkbox" />
            <button type="button">Subtitles</button>
            <div role="slider" tabIndex={0} aria-label="Playback position" aria-valuenow={0} />
            <PlayerKeyIndicator indicator={indicator} />
        </div>
    );
}

const press = (key: string, target: EventTarget = window, init: KeyboardEventInit = {}) => {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
    act(() => { target.dispatchEvent(event); });
    return event;
};

beforeEach(() => {
    localStorage.clear();
    vi.useFakeTimers();
});

afterEach(() => {
    vi.useRealTimers();
});

describe('usePlayerKeyboard', () => {
    it('skips forward and back 10 seconds and keeps the page from scrolling', () => {
        const onSkipForward = vi.fn();
        const onSkipBackward = vi.fn();
        render(<Harness onSkipForward={onSkipForward} onSkipBackward={onSkipBackward} />);

        const right = press('ArrowRight');
        const left = press('ArrowLeft');

        expect(onSkipForward).toHaveBeenCalledWith(10);
        expect(onSkipBackward).toHaveBeenCalledWith(10);
        expect(right.defaultPrevented).toBe(true);
        expect(left.defaultPrevented).toBe(true);
    });

    it('raises and lowers the volume by 5%', () => {
        const onVolume = vi.fn();
        render(<Harness startVolume={0.5} onVolume={onVolume} />);

        const up = press('ArrowUp');
        press('ArrowDown');
        press('ArrowDown');

        expect(onVolume.mock.calls.map(([level]) => level)).toEqual([0.55, 0.5, 0.45]);
        expect(up.defaultPrevented).toBe(true);
    });

    it('steps from the level it just set when presses come faster than a render', () => {
        const onVolume = vi.fn();
        render(<Harness startVolume={0.5} onVolume={onVolume} />);

        act(() => {
            window.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', cancelable: true }));
            window.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', cancelable: true }));
        });

        expect(onVolume.mock.calls.map(([level]) => level)).toEqual([0.55, 0.6]);
    });

    it('uses the steps saved in the playback settings', () => {
        localStorage.setItem(StorageKeys.deviceId, 'device');
        localStorage.setItem(StorageKeys.playbackPrefs('unknown', 'device'), JSON.stringify({ seekStepSeconds: 30, volumeStepPercent: 10 }));
        const onSkipForward = vi.fn();
        const onVolume = vi.fn();
        render(<Harness onSkipForward={onSkipForward} onVolume={onVolume} />);

        press('ArrowRight');
        press('ArrowUp');

        expect(onSkipForward).toHaveBeenCalledWith(30);
        expect(onVolume).toHaveBeenCalledWith(0.6);
    });

    it('does nothing while the player is minimised', () => {
        const onSkipForward = vi.fn();
        render(<Harness enabled={false} onSkipForward={onSkipForward} />);

        const event = press('ArrowRight');

        expect(onSkipForward).not.toHaveBeenCalled();
        expect(event.defaultPrevented).toBe(false);
    });

    it('leaves the arrow keys to a text field being typed in', () => {
        const onSkipBackward = vi.fn();
        render(<Harness onSkipBackward={onSkipBackward} />);

        const event = press('ArrowLeft', screen.getByLabelText('Search'));

        expect(onSkipBackward).not.toHaveBeenCalled();
        expect(event.defaultPrevented).toBe(false);
    });

    it('takes the keys from a focused volume slider so they act the same everywhere', () => {
        const onSkipForward = vi.fn();
        render(<Harness onSkipForward={onSkipForward} />);

        const event = press('ArrowRight', screen.getByLabelText('Volume'));

        expect(onSkipForward).toHaveBeenCalledWith(10);
        expect(event.defaultPrevented).toBe(true);
    });

    it('stands down while a modal dialog is open', () => {
        const onSkipForward = vi.fn();
        render(<Harness onSkipForward={onSkipForward} />);
        const dialog = document.createElement('div');
        dialog.setAttribute('aria-modal', 'true');
        document.body.appendChild(dialog);

        const event = press('ArrowRight');

        expect(onSkipForward).not.toHaveBeenCalled();
        expect(event.defaultPrevented).toBe(false);
        dialog.remove();
    });

    it('leaves Left and Right alone on a stream that cannot seek', () => {
        const onSkipForward = vi.fn();
        const onVolume = vi.fn();
        render(<Harness canSeek={false} onSkipForward={onSkipForward} onVolume={onVolume} />);

        const right = press('ArrowRight');
        press('ArrowUp');

        expect(onSkipForward).not.toHaveBeenCalled();
        expect(right.defaultPrevented).toBe(false);
        expect(onVolume).toHaveBeenCalledWith(0.55);
    });

    it('pauses and plays on Space and keeps the page from scrolling', () => {
        const onPlayPause = vi.fn();
        render(<Harness onPlayPause={onPlayPause} />);

        const pause = press(' ');
        press(' ');

        expect(onPlayPause).toHaveBeenCalledTimes(2);
        expect(pause.defaultPrevented).toBe(true);
    });

    it('toggles once while Space is held down', () => {
        const onPlayPause = vi.fn();
        render(<Harness onPlayPause={onPlayPause} />);

        press(' ');
        const held = press(' ', window, { repeat: true });
        press(' ', window, { repeat: true });

        expect(onPlayPause).toHaveBeenCalledTimes(1);
        expect(held.defaultPrevented).toBe(true);
    });

    it('leaves Space to a focused button, box or text field', () => {
        const onPlayPause = vi.fn();
        render(<Harness onPlayPause={onPlayPause} />);

        const onButton = press(' ', screen.getByRole('button', { name: 'Subtitles' }));
        press(' ', screen.getByLabelText('Remember'));
        press(' ', screen.getByLabelText('Search'));

        expect(onPlayPause).not.toHaveBeenCalled();
        expect(onButton.defaultPrevented).toBe(false);
    });

    it('takes Space from a focused seek bar or volume slider', () => {
        const onPlayPause = vi.fn();
        render(<Harness onPlayPause={onPlayPause} />);

        press(' ', screen.getByRole('slider', { name: 'Playback position' }));
        press(' ', screen.getByLabelText('Volume'));

        expect(onPlayPause).toHaveBeenCalledTimes(2);
    });

    it('answers only Shift with an arrow where the plain arrows move focus, and still plays on Space', () => {
        const onSkipForward = vi.fn();
        const onVolume = vi.fn();
        const onPlayPause = vi.fn();
        render(<Harness plainArrows={false} onSkipForward={onSkipForward} onVolume={onVolume} onPlayPause={onPlayPause} />);

        const plain = press('ArrowRight');
        press('ArrowUp');
        const shifted = press('ArrowRight', window, { shiftKey: true });
        press('ArrowUp', window, { shiftKey: true });
        press(' ');

        expect(plain.defaultPrevented).toBe(false);
        expect(onSkipForward).toHaveBeenCalledTimes(1);
        expect(shifted.defaultPrevented).toBe(true);
        expect(onVolume).toHaveBeenCalledWith(0.55);
        expect(onPlayPause).toHaveBeenCalledTimes(1);
    });

    it('does not play or pause while the player is minimised', () => {
        const onPlayPause = vi.fn();
        render(<Harness enabled={false} onPlayPause={onPlayPause} />);

        const event = press(' ');

        expect(onPlayPause).not.toHaveBeenCalled();
        expect(event.defaultPrevented).toBe(false);
    });

    it('ignores a key another handler already took', () => {
        const onSkipForward = vi.fn();
        render(<Harness onSkipForward={onSkipForward} />);
        const menu = document.createElement('button');
        document.body.appendChild(menu);
        menu.addEventListener('keydown', event => event.preventDefault());

        press('ArrowRight', menu);

        expect(onSkipForward).not.toHaveBeenCalled();
        menu.remove();
    });
});

describe('PlayerKeyIndicator', () => {
    it('shows the seek, adding up presses in the same direction', () => {
        render(<Harness />);

        press('ArrowRight');
        expect(screen.getByTestId('player-key-indicator')).toHaveTextContent('+10s');

        press('ArrowRight');
        press('ArrowRight');
        expect(screen.getByTestId('player-key-indicator')).toHaveTextContent('+30s');
        expect(screen.getByRole('status')).toHaveTextContent('Forward 30 seconds');
    });

    it('starts again when the direction changes', () => {
        render(<Harness />);

        press('ArrowRight');
        press('ArrowLeft');

        expect(screen.getByTestId('player-key-indicator')).toHaveTextContent('−10s');
        expect(screen.getByRole('status')).toHaveTextContent('Back 10 seconds');
    });

    it('says whether Space paused or played', () => {
        render(<Harness startPlaying />);

        press(' ');
        expect(screen.getByTestId('player-key-indicator')).toHaveTextContent('Paused');
        expect(screen.getByRole('status')).toHaveTextContent('Paused');

        press(' ');
        expect(screen.getByTestId('player-key-indicator')).toHaveTextContent('Playing');
        expect(screen.getByRole('status')).toHaveTextContent('Playing');
    });

    it('shows the volume level', () => {
        render(<Harness startVolume={0.5} />);

        press('ArrowUp');

        expect(screen.getByTestId('player-key-indicator')).toHaveTextContent('55%');
        expect(screen.getByRole('status')).toHaveTextContent('Volume 55%');
    });

    it('goes away shortly after the last press, and the next press starts fresh', () => {
        render(<Harness />);

        press('ArrowRight');
        act(() => { vi.advanceTimersByTime(600); });
        press('ArrowRight');
        act(() => { vi.advanceTimersByTime(600); });
        expect(screen.getByTestId('player-key-indicator')).toHaveTextContent('+20s');

        act(() => { vi.advanceTimersByTime(400); });
        expect(screen.queryByTestId('player-key-indicator')).toBeNull();

        press('ArrowRight');
        expect(screen.getByTestId('player-key-indicator')).toHaveTextContent('+10s');
    });
});
