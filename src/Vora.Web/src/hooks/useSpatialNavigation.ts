import { useEffect } from 'react';
import { directionFromKey, findNeighbour, ownsDirection, type NavRect } from '../utils/spatialNavigation';

const FocusableSelector = [
    'button:not([disabled])',
    'a[href]',
    'input:not([disabled])',
    'select:not([disabled])',
    'textarea:not([disabled])',
    '[tabindex]:not([tabindex="-1"])',
].join(',');

function isVisible(element: HTMLElement): boolean {
    // A control inside a collapsed panel is still in the DOM and still matches
    // the selector, and focusing one the viewer cannot see looks like the focus
    // ring vanishing.
    return element.offsetWidth > 0 || element.offsetHeight > 0 || element.getClientRects().length > 0;
}

function collect(container: HTMLElement): { element: HTMLElement; rect: NavRect }[] {
    return Array.from(container.querySelectorAll<HTMLElement>(FocusableSelector))
        .filter(isVisible)
        .map(element => ({ element, rect: element.getBoundingClientRect() }));
}

// Moves focus with the arrow keys inside `container`.
//
// Browsers only move focus on Tab; there is no arrow-key spatial navigation to
// inherit. A TV remote's D-pad sends arrow keys, so without this the focus ring
// never moves off whatever it landed on and every OK press activates that one
// control.
//
// Rects are read at keypress time rather than cached: the now-playing screen
// opens and closes panels (queue, lyrics, audio settings) that move everything
// around, and a cached layout would send focus to where a control used to be.
export function useSpatialNavigation(
    container: React.RefObject<HTMLElement | null>,
    enabled: boolean,
): void {
    useEffect(() => {
        if (!enabled) return;

        const onKeyDown = (event: KeyboardEvent) => {
            const direction = directionFromKey(event.key);
            if (!direction) return;
            if (event.altKey || event.ctrlKey || event.metaKey) return;

            const root = container.current;
            if (!root) return;

            const active = document.activeElement;
            if (!(active instanceof HTMLElement) || !root.contains(active)) return;

            // The seek bar scrubs on Left/Right, which is what a remote should
            // do; Up/Down still moves focus off it.
            if (ownsDirection(active, direction)) return;

            const candidates = collect(root).filter(candidate => candidate.element !== active);
            const target = findNeighbour(active.getBoundingClientRect(), candidates, direction);
            if (!target) return;

            event.preventDefault();
            target.element.focus();
        };

        window.addEventListener('keydown', onKeyDown);
        return () => window.removeEventListener('keydown', onKeyDown);
    }, [container, enabled]);
}
