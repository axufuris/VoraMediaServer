import { StorageKeys } from './storageKeys';

export interface PictureCrop {
    top: number;
    right: number;
    bottom: number;
    left: number;
}

export type PictureFit = 'fit' | 'fill';

export interface Size {
    width: number;
    height: number;
}

export interface PictureLayout {
    width: number;
    height: number;
    left: number;
    top: number;
}

export interface CroppedTrack {
    width?: number;
    height?: number;
    cropTop?: number | null;
    cropRight?: number | null;
    cropBottom?: number | null;
    cropLeft?: number | null;
}

export const NoCrop: PictureCrop = { top: 0, right: 0, bottom: 0, left: 0 };

export function isNoCrop(crop: PictureCrop): boolean {
    return crop.top === 0 && crop.right === 0 && crop.bottom === 0 && crop.left === 0;
}

export function cropOf(track: CroppedTrack | undefined): PictureCrop {
    if (!track?.width || !track.height) return NoCrop;
    const { cropTop, cropRight, cropBottom, cropLeft } = track;
    if (cropTop == null || cropRight == null || cropBottom == null || cropLeft == null) return NoCrop;
    if (cropTop + cropBottom >= track.height || cropLeft + cropRight >= track.width) return NoCrop;
    if (cropTop === 0 && cropRight === 0 && cropBottom === 0 && cropLeft === 0) return NoCrop;
    return {
        top: cropTop / track.height,
        right: cropRight / track.width,
        bottom: cropBottom / track.height,
        left: cropLeft / track.width,
    };
}

export function pictureLayout(viewport: Size, frame: Size, crop: PictureCrop, fit: PictureFit): PictureLayout | null {
    if (viewport.width <= 0 || viewport.height <= 0 || frame.width <= 0 || frame.height <= 0) return null;
    const pictureWidth = frame.width * (1 - crop.left - crop.right);
    const pictureHeight = frame.height * (1 - crop.top - crop.bottom);
    if (pictureWidth <= 0 || pictureHeight <= 0) return null;

    const widthScale = viewport.width / pictureWidth;
    const heightScale = viewport.height / pictureHeight;
    const scale = fit === 'fill' ? Math.max(widthScale, heightScale) : Math.min(widthScale, heightScale);
    const width = frame.width * scale;
    const height = frame.height * scale;

    return {
        width,
        height,
        left: (viewport.width - pictureWidth * scale) / 2 - crop.left * width,
        top: (viewport.height - pictureHeight * scale) / 2 - crop.top * height,
    };
}

export function sameLayout(a: PictureLayout | null, b: PictureLayout | null): boolean {
    if (!a || !b) return a === b;
    return Math.abs(a.width - b.width) < 1 && Math.abs(a.height - b.height) < 1
        && Math.abs(a.left - b.left) < 1 && Math.abs(a.top - b.top) < 1;
}

export function readPictureFit(): PictureFit {
    try {
        return localStorage.getItem(StorageKeys.playerPictureFit) === 'fill' ? 'fill' : 'fit';
    } catch {
        return 'fit';
    }
}

export function savePictureFit(fit: PictureFit): void {
    try {
        localStorage.setItem(StorageKeys.playerPictureFit, fit);
    } catch {
        return;
    }
}
