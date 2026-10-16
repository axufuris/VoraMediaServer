import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import { useState } from 'react';
import { usePlayerKeyboard } from './usePlayerKeyboard';
import PlayerKeyIndicator from './PlayerKeyIndicator';
import { StorageKeys } from '../../../utils/storageKeys';

interface HarnessProps {
    enabled?: boolean;
    canSeek?: boolean;
    startVolume?: number;
    onSkipForward?: (seconds: number) => void;
    onSkipBackward?: (seconds: number) => void;
    onVolume?: (volume: number) => void;
}

function Harness({ enabled = true, canSeek = true, startVolume = 0.5, onSkipForward = vi.fn(), onSkipBackward = vi.fn(), onVolume }: HarnessProps) {
    const [volume, setVolume] = useState(startVolume);
    const indicator = usePlayerKeyboard({
        enabled,
        canSeek,
        volume,
        onSkipForward,
        onSkipBackward,
        onVolume: level => { setVolume(level); onVolume?.(level); },
    });
    return (
        <div>
            <input aria-label="Search" />
            <input aria-label="Volume" type="range" />
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
