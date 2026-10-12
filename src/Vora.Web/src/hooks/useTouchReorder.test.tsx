import { describe, it, expect, vi, beforeAll, afterAll, beforeEach, afterEach } from 'vitest';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { reorderListProps, reorderRowProps, useTouchReorder } from './useTouchReorder';

class TestPointerEvent extends MouseEvent {
    readonly pointerId: number;
    readonly pointerType: string;

    constructor(type: string, init: PointerEventInit = {}) {
        super(type, init);
        this.pointerId = init.pointerId ?? 0;
        this.pointerType = init.pointerType ?? '';
    }
}

const ROW_HEIGHT = 40;
const IDS = ['music', 'shows', 'movies', 'radio'];

function List({ onMove }: { onMove: (from: number, to: number) => void }) {
    const touch = useTouchReorder(onMove);
    return (
        <ul {...reorderListProps}>
            {IDS.map((id, index) => (
                <li key={id} {...reorderRowProps(index)} data-testid={`row-${id}`} style={touch.rowStyle(index)}>
                    <span data-testid={`handle-${id}`} {...touch.handleProps(index)} />
                    {id}
                </li>
            ))}
        </ul>
    );
}

const rowFor = (y: number) => screen.queryByTestId(`row-${IDS[Math.floor(y / ROW_HEIGHT)]}`);

const pointer = (type: 'pointermove' | 'pointerup' | 'pointercancel', y: number) => {
    act(() => {
        window.dispatchEvent(new PointerEvent(type, { pointerId: 1, pointerType: 'touch', clientX: 10, clientY: y, bubbles: true }));
    });
};

describe('useTouchReorder', () => {
    beforeAll(() => vi.stubGlobal('PointerEvent', TestPointerEvent));
    afterAll(() => vi.unstubAllGlobals());

    beforeEach(() => {
        document.elementFromPoint = vi.fn((_x: number, y: number) => rowFor(y));
        vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
            const index = Number(this.getAttribute('data-reorder-index') ?? 0);
            return DOMRect.fromRect({ x: 0, y: index * ROW_HEIGHT, width: 300, height: ROW_HEIGHT });
        });
    });

    afterEach(() => vi.restoreAllMocks());

    it('moves a row to where a finger drops it', () => {
        const onMove = vi.fn();
        render(<List onMove={onMove} />);

        fireEvent.pointerDown(screen.getByTestId('handle-music'), { pointerId: 1, pointerType: 'touch', clientX: 10, clientY: 20 });
        pointer('pointermove', 70);
        pointer('pointermove', 150);
        pointer('pointerup', 150);

        expect(onMove).toHaveBeenCalledWith(0, 3);
    });

    it('marks the dragged row and the drop point while dragging', () => {
        render(<List onMove={vi.fn()} />);

        fireEvent.pointerDown(screen.getByTestId('handle-radio'), { pointerId: 1, pointerType: 'touch', clientX: 10, clientY: 140 });
        pointer('pointermove', 45);

        expect(screen.getByTestId('row-radio')).toHaveStyle({ opacity: '0.4' });
        expect(screen.getByTestId('row-shows').style.boxShadow).toContain('var(--vora-accent-500)');

        pointer('pointercancel', 45);
        expect(screen.getByTestId('row-radio')).not.toHaveStyle({ opacity: '0.4' });
    });

    it('does nothing when the finger lifts where it started or the drag is cancelled', () => {
        const onMove = vi.fn();
        render(<List onMove={onMove} />);

        fireEvent.pointerDown(screen.getByTestId('handle-shows'), { pointerId: 1, pointerType: 'touch', clientX: 10, clientY: 50 });
        pointer('pointerup', 55);
        fireEvent.pointerDown(screen.getByTestId('handle-shows'), { pointerId: 1, pointerType: 'touch', clientX: 10, clientY: 50 });
        pointer('pointermove', 150);
        pointer('pointercancel', 150);

        expect(onMove).not.toHaveBeenCalled();
    });

    it('leaves mouse drags to the browser', () => {
        const onMove = vi.fn();
        render(<List onMove={onMove} />);

        fireEvent.pointerDown(screen.getByTestId('handle-music'), { pointerId: 1, pointerType: 'mouse', clientX: 10, clientY: 20 });
        pointer('pointermove', 130);
        pointer('pointerup', 130);

        expect(onMove).not.toHaveBeenCalled();
        expect(screen.getByTestId('row-music')).not.toHaveStyle({ opacity: '0.4' });
    });
});
