import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useLocation, useNavigationType } from 'react-router-dom';

export const SCROLL_RESTORE_TIMEOUT_MS = 8000;

const positions = new Map<string, number>();

export function forgetScrollPositions() {
    positions.clear();
}

// The client scrolls inside its layout's <main>, not the window, so the
// browser's own restoration never applies. Each history entry keeps the
// position it was left at. Back and Forward put it back once the page has
// loaded enough to reach it; opening a new page starts at the top.
export function useScrollRestoration(): (node: HTMLElement | null) => void {
    const location = useLocation();
    const navigationType = useNavigationType();
    const containerRef = useRef<HTMLElement | null>(null);
    const [attachments, setAttachments] = useState(0);
    const keyRef = useRef(location.key);
    const pathnameRef = useRef(location.pathname);
    const restoringRef = useRef(false);

    const attach = useCallback((node: HTMLElement | null) => {
        if (containerRef.current === node) return;
        containerRef.current = node;
        setAttachments(count => count + 1);
    }, []);

    useEffect(() => {
        const container = containerRef.current;
        if (!container) return;
        const remember = () => {
            if (!restoringRef.current) positions.set(keyRef.current, container.scrollTop);
        };
        container.addEventListener('scroll', remember, { passive: true });
        return () => container.removeEventListener('scroll', remember);
    }, [attachments]);

    useLayoutEffect(() => {
        keyRef.current = location.key;
        const samePage = pathnameRef.current === location.pathname;
        pathnameRef.current = location.pathname;
        const container = containerRef.current;
        if (!container) return;

        if (navigationType !== 'POP') {
            if (!samePage) container.scrollTop = 0;
            return;
        }

        const target = positions.get(location.key);
        if (target === undefined) return;

        restoringRef.current = true;
        const startedAt = performance.now();
        let frame = 0;

        const stop = () => {
            restoringRef.current = false;
            cancelAnimationFrame(frame);
            container.removeEventListener('wheel', stop);
            container.removeEventListener('touchstart', stop);
            container.removeEventListener('pointerdown', stop);
            window.removeEventListener('keydown', stop);
        };

        const attempt = () => {
            const reachable = container.scrollHeight - container.clientHeight;
            if (reachable >= target || performance.now() - startedAt >= SCROLL_RESTORE_TIMEOUT_MS) {
                container.scrollTop = Math.min(target, Math.max(reachable, 0));
                stop();
                return;
            }
            frame = requestAnimationFrame(attempt);
        };

        container.addEventListener('wheel', stop, { passive: true });
        container.addEventListener('touchstart', stop, { passive: true });
        container.addEventListener('pointerdown', stop);
        window.addEventListener('keydown', stop);
        attempt();
        return stop;
    }, [location.key, location.pathname, navigationType, attachments]);

    return attach;
}
