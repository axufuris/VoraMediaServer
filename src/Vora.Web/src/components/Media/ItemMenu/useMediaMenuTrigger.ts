import { useContext, useEffect, useRef, type CSSProperties, type MouseEvent, type PointerEvent } from 'react';
import { MediaItemMenuContext, type MediaMenuTarget } from './mediaItemMenu';

export const LONG_PRESS_MS = 500;
// A finger never holds perfectly still. Past this it is a scroll or a drag.
const MOVE_TOLERANCE_PX = 10;

export interface MediaMenuTriggerProps {
    onContextMenu: (event: MouseEvent<HTMLElement>) => void;
    onPointerDown: (event: PointerEvent<HTMLElement>) => void;
    onPointerMove: (event: PointerEvent<HTMLElement>) => void;
    onPointerUp: () => void;
    onPointerCancel: () => void;
    onPointerLeave: () => void;
    onClickCapture: (event: MouseEvent<HTMLElement>) => void;
    style: CSSProperties;
}

interface Press {
    timer: number | null;
    x: number;
    y: number;
}

// Props for anything that stands for a title: spread them on the element and a
// right-click, the keyboard's menu key, or a long press with a finger opens the
// title's menu. Returns nothing when there is no title or no menu host, so the
// element keeps the browser's own menu.
export function useMediaMenuTrigger(target: MediaMenuTarget | null | undefined): MediaMenuTriggerProps | undefined {
    const menu = useContext(MediaItemMenuContext);
    const press = useRef<Press>({ timer: null, x: 0, y: 0 });
    const swallowNextClick = useRef(false);

    const cancelPress = () => {
        const held = press.current;
        if (held.timer === null) return;
        window.clearTimeout(held.timer);
        held.timer = null;
    };

    useEffect(() => {
        const held = press.current;
        return () => {
            if (held.timer !== null) window.clearTimeout(held.timer);
        };
    }, []);

    if (!menu || !target) return undefined;

    return {
        onContextMenu: event => {
            event.preventDefault();
            event.stopPropagation();
            cancelPress();
            // The keyboard's menu key reports no position, so open over the
            // element instead of in the corner of the window.
            const box = event.currentTarget.getBoundingClientRect();
            const hasPosition = event.clientX !== 0 || event.clientY !== 0;
            menu.open(target, hasPosition
                ? { x: event.clientX, y: event.clientY }
                : { x: box.left + box.width / 2, y: box.top + box.height / 2 });
        },
        onPointerDown: event => {
            swallowNextClick.current = false;
            if (event.pointerType === 'mouse') return;
            cancelPress();
            const held = press.current;
            const { clientX: x, clientY: y } = event;
            held.x = x;
            held.y = y;
            held.timer = window.setTimeout(() => {
                held.timer = null;
                // Lifting the finger still produces a click, which would open
                // the title behind the menu.
                swallowNextClick.current = true;
                menu.open(target, { x, y });
            }, LONG_PRESS_MS);
        },
        onPointerMove: event => {
            const held = press.current;
            if (held.timer === null) return;
            if (Math.abs(event.clientX - held.x) > MOVE_TOLERANCE_PX || Math.abs(event.clientY - held.y) > MOVE_TOLERANCE_PX) {
                cancelPress();
            }
        },
        onPointerUp: cancelPress,
        onPointerCancel: cancelPress,
        onPointerLeave: cancelPress,
        onClickCapture: event => {
            if (!swallowNextClick.current) return;
            swallowNextClick.current = false;
            event.preventDefault();
            event.stopPropagation();
        },
        // Stops the phone's own long-press sheet (save image, copy link) and
        // text selection from opening over this menu.
        style: { WebkitTouchCallout: 'none', WebkitUserSelect: 'none', userSelect: 'none' },
    };
}
