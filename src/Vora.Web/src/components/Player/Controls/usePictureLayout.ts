import { type RefObject, useEffect, useMemo, useState } from 'react';
import { isNoCrop, pictureLayout, sameLayout, type PictureCrop, type PictureFit, type PictureLayout, type Size } from '../../../utils/pictureLayout';

interface PictureLayoutOptions {
    containerRef: RefObject<HTMLElement | null>;
    videoRef: RefObject<HTMLVideoElement | null>;
    videoKey: string | undefined;
    crop: PictureCrop;
    fit: PictureFit;
    enabled: boolean;
}

export interface PictureView {
    layout: PictureLayout | null;
    viewport: Size;
    canFill: boolean;
}

const sameSize = (a: Size, b: Size) => a.width === b.width && a.height === b.height;

export function usePictureLayout({ containerRef, videoRef, videoKey, crop, fit, enabled }: PictureLayoutOptions): PictureView {
    const [viewport, setViewport] = useState<Size>({ width: 0, height: 0 });
    const [frame, setFrame] = useState<Size>({ width: 0, height: 0 });

    useEffect(() => {
        const container = containerRef.current;
        if (!enabled || !container) return;

        const measure = () => {
            const next = { width: container.clientWidth, height: container.clientHeight };
            setViewport(current => (sameSize(current, next) ? current : next));
        };
        measure();

        if (typeof ResizeObserver === 'undefined') {
            window.addEventListener('resize', measure);
            return () => window.removeEventListener('resize', measure);
        }
        const observer = new ResizeObserver(measure);
        observer.observe(container);
        return () => observer.disconnect();
    }, [containerRef, enabled]);

    useEffect(() => {
        const video = videoRef.current;
        if (!video) return;

        const read = () => {
            const next = { width: video.videoWidth, height: video.videoHeight };
            setFrame(current => (sameSize(current, next) ? current : next));
        };
        read();
        video.addEventListener('loadedmetadata', read);
        video.addEventListener('resize', read);
        return () => {
            video.removeEventListener('loadedmetadata', read);
            video.removeEventListener('resize', read);
        };
    }, [videoRef, videoKey]);

    const { top, right, bottom, left } = crop;

    return useMemo(() => {
        if (!enabled) return { layout: null, viewport, canFill: false };

        const area = { top, right, bottom, left };
        const fitted = pictureLayout(viewport, frame, area, 'fit');
        const filled = pictureLayout(viewport, frame, area, 'fill');
        const canFill = !sameLayout(fitted, filled);
        const chosen = fit === 'fill' && canFill ? filled : fitted;
        const browserDoesIt = fit === 'fit' && isNoCrop(area);

        return { layout: browserDoesIt ? null : chosen, viewport, canFill };
    }, [enabled, viewport, frame, top, right, bottom, left, fit]);
}
