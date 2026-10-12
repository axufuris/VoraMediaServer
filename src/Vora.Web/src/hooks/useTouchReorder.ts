import { useCallback, useEffect, useRef, useState, type CSSProperties, type PointerEvent as ReactPointerEvent } from 'react';
import { dropIndex } from '../utils/navOrder';

export interface TouchDrag {
    from: number;
    over: number;
    after: boolean;
}

const LIST_ATTRIBUTE = 'data-reorder-list';
const ROW_ATTRIBUTE = 'data-reorder-index';
const SCROLL_EDGE = 56;
const SCROLL_STEP = 12;
const MOVE_THRESHOLD = 6;

export const reorderListProps = { [LIST_ATTRIBUTE]: '' };

export const reorderRowProps = (index: number) => ({ [ROW_ATTRIBUTE]: index });

const scrollParentOf = (element: Element): HTMLElement | null => {
    for (let node = element.parentElement; node; node = node.parentElement) {
        const { overflowY } = getComputedStyle(node);
        if ((overflowY === 'auto' || overflowY === 'scroll') && node.scrollHeight > node.clientHeight) return node;
    }
    return null;
};

const rowAt = (list: Element, x: number, y: number): { index: number; after: boolean } | null => {
    const hit = document.elementFromPoint(x, y)?.closest(`[${ROW_ATTRIBUTE}]`);
    if (!hit || hit.closest(`[${LIST_ATTRIBUTE}]`) !== list) return null;
    const index = Number(hit.getAttribute(ROW_ATTRIBUTE));
    if (Number.isNaN(index)) return null;
    const box = hit.getBoundingClientRect();
    return { index, after: y > box.top + box.height / 2 };
};

const swallowNextClick = () => {
    const stop = (e: MouseEvent) => {
        e.stopPropagation();
        e.preventDefault();
    };
    window.addEventListener('click', stop, { capture: true, once: true });
    setTimeout(() => window.removeEventListener('click', stop, { capture: true }), 0);
};

export function useTouchReorder(onMove: (from: number, to: number) => void) {
    const [drag, setDrag] = useState<TouchDrag | null>(null);
    const onMoveRef = useRef(onMove);
    const cleanupRef = useRef<(() => void) | null>(null);

    useEffect(() => {
        onMoveRef.current = onMove;
    });

    useEffect(() => () => cleanupRef.current?.(), []);

    const startDrag = useCallback((e: ReactPointerEvent<HTMLElement>, index: number) => {
        if (e.pointerType === 'mouse' || e.button !== 0) return;
        const handle = e.currentTarget;
        const found = handle.closest(`[${LIST_ATTRIBUTE}]`);
        if (!found) return;
        const list: Element = found;

        e.preventDefault();
        e.stopPropagation();
        cleanupRef.current?.();

        const pointerId = e.pointerId;
        const startY = e.clientY;
        const scroller = scrollParentOf(list);
        let current: TouchDrag = { from: index, over: index, after: false };
        let lastX = e.clientX;
        let lastY = e.clientY;
        let moved = false;
        let frame = 0;

        function update() {
            const row = rowAt(list, lastX, lastY);
            if (!row) return;
            if (row.index !== current.over || row.after !== current.after) {
                current = { ...current, over: row.index, after: row.after };
                setDrag(current);
            }
        }

        function autoScroll() {
            if (scroller) {
                const box = scroller.getBoundingClientRect();
                const step = lastY < box.top + SCROLL_EDGE ? -SCROLL_STEP : lastY > box.bottom - SCROLL_EDGE ? SCROLL_STEP : 0;
                if (step !== 0) {
                    scroller.scrollTop += step;
                    update();
                }
            }
            frame = requestAnimationFrame(autoScroll);
        }

        function cleanup() {
            cancelAnimationFrame(frame);
            window.removeEventListener('pointermove', onPointerMove);
            window.removeEventListener('pointerup', onPointerUp);
            window.removeEventListener('pointercancel', onPointerCancel);
            if (handle.hasPointerCapture?.(pointerId)) handle.releasePointerCapture(pointerId);
            cleanupRef.current = null;
        }

        function finish(commit: boolean) {
            cleanup();
            setDrag(null);
            if (!commit) return;
            if (moved) swallowNextClick();
            const to = dropIndex(current.from, current.over, current.after);
            if (to !== current.from) onMoveRef.current(current.from, to);
        }

        function onPointerMove(event: PointerEvent) {
            if (event.pointerId !== pointerId) return;
            event.preventDefault();
            lastX = event.clientX;
            lastY = event.clientY;
            if (Math.abs(lastY - startY) > MOVE_THRESHOLD) moved = true;
            update();
        }

        function onPointerUp(event: PointerEvent) {
            if (event.pointerId === pointerId) finish(true);
        }

        function onPointerCancel(event: PointerEvent) {
            if (event.pointerId === pointerId) finish(false);
        }

        handle.setPointerCapture?.(pointerId);
        window.addEventListener('pointermove', onPointerMove, { passive: false });
        window.addEventListener('pointerup', onPointerUp);
        window.addEventListener('pointercancel', onPointerCancel);
        cleanupRef.current = cleanup;
        frame = requestAnimationFrame(autoScroll);
        setDrag(current);
    }, []);

    const handleProps = useCallback((index: number) => ({
        onPointerDown: (e: ReactPointerEvent<HTMLElement>) => startDrag(e, index),
    }), [startDrag]);

    const rowStyle = useCallback((index: number): CSSProperties | undefined => {
        if (!drag) return undefined;
        if (index === drag.from) return { opacity: 0.4 };
        if (index !== drag.over || dropIndex(drag.from, drag.over, drag.after) === drag.from) return undefined;
        return { boxShadow: `inset 0 ${drag.after ? -2 : 2}px 0 var(--vora-accent-500)` };
    }, [drag]);

    return { drag, handleProps, rowStyle };
}

export type TouchReorder = ReturnType<typeof useTouchReorder>;
