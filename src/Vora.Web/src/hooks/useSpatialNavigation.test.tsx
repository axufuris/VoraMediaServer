import { describe, it, expect, vi, afterEach } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { useRef } from 'react';
import { useSpatialNavigation } from './useSpatialNavigation';

function Row() {
    const ref = useRef<HTMLDivElement>(null);
    useSpatialNavigation(ref, true);
    return (
        <div ref={ref}>
            <button type="button">Previous</button>
            <button type="button">Play</button>
            <button type="button">Next</button>
        </div>
    );
}

const placeInARow = () => {
    ['Previous', 'Play', 'Next'].forEach((name, index) => {
        const button = screen.getByRole('button', { name });
        const left = index * 100;
        vi.spyOn(button, 'getBoundingClientRect').mockReturnValue({ left, right: left + 60, top: 0, bottom: 60, width: 60, height: 60, x: left, y: 0, toJSON: () => ({}) } as DOMRect);
        Object.defineProperty(button, 'offsetWidth', { value: 60, configurable: true });
    });
};

describe('useSpatialNavigation', () => {
    afterEach(() => vi.restoreAllMocks());

    it('moves focus along the row with a D-pad press', () => {
        render(<Row />);
        placeInARow();
        screen.getByRole('button', { name: 'Play' }).focus();

        fireEvent.keyDown(document.activeElement ?? window, { key: 'ArrowRight' });

        expect(screen.getByRole('button', { name: 'Next' })).toHaveFocus();
    });

    it('leaves Shift with an arrow to the player, which seeks or changes the volume with it', () => {
        render(<Row />);
        placeInARow();
        screen.getByRole('button', { name: 'Play' }).focus();

        fireEvent.keyDown(document.activeElement ?? window, { key: 'ArrowRight', shiftKey: true });

        expect(screen.getByRole('button', { name: 'Play' })).toHaveFocus();
    });
});
