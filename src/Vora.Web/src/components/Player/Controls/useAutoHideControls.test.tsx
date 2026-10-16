import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { useAutoHideControls } from './useAutoHideControls';

function Controls({ isMinimized = false }: { isMinimized?: boolean }) {
    const visible = useAutoHideControls({ isMinimized, isPlaying: true, delayMs: 1000 });
    return <span>{visible ? 'controls shown' : 'controls hidden'}</span>;
}

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

it('hides the controls a while after the mouse stops', () => {
    render(<Controls />);

    fireEvent.mouseMove(window);
    act(() => { vi.advanceTimersByTime(1000); });

    expect(screen.getByText('controls hidden')).toBeInTheDocument();
});

it('brings the controls back on a key press, so an arrow-key seek shows where it landed', () => {
    render(<Controls />);
    fireEvent.mouseMove(window);
    act(() => { vi.advanceTimersByTime(1000); });

    fireEvent.keyDown(window, { key: 'ArrowRight' });

    expect(screen.getByText('controls shown')).toBeInTheDocument();
    act(() => { vi.advanceTimersByTime(1000); });
    expect(screen.getByText('controls hidden')).toBeInTheDocument();
});
