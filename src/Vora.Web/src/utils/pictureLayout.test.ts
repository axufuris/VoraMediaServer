import { afterEach, describe, expect, it } from 'vitest';
import { cropOf, isNoCrop, NoCrop, pictureLayout, readPictureFit, sameLayout, savePictureFit } from './pictureLayout';

const window16x9 = { width: 1600, height: 900 };
const frame1080 = { width: 1920, height: 1080 };
const letterbox = cropOf({ width: 1920, height: 1080, cropTop: 60, cropRight: 0, cropBottom: 60, cropLeft: 0 });

describe('the crop of a video track', () => {
    it('is the share of the frame each bar takes', () => {
        expect(letterbox).toEqual({ top: 60 / 1080, right: 0, bottom: 60 / 1080, left: 0 });
    });

    it('is none until the bars were looked for, when there are none, or when it makes no sense', () => {
        expect(cropOf({ width: 1920, height: 1080 })).toBe(NoCrop);
        expect(cropOf({ width: 1920, height: 1080, cropTop: 60, cropRight: 0, cropBottom: null, cropLeft: 0 })).toBe(NoCrop);
        expect(cropOf({ width: 1920, height: 1080, cropTop: 600, cropRight: 0, cropBottom: 600, cropLeft: 0 })).toBe(NoCrop);
        expect(cropOf({ cropTop: 60, cropRight: 0, cropBottom: 60, cropLeft: 0 })).toBe(NoCrop);
        expect(cropOf(undefined)).toBe(NoCrop);
        expect(isNoCrop(cropOf({ width: 1920, height: 1080, cropTop: 0, cropRight: 0, cropBottom: 0, cropLeft: 0 }))).toBe(true);
    });
});

describe('laying out the picture', () => {
    it('fits a whole frame without bars as object-contain would', () => {
        const layout = pictureLayout({ width: 1600, height: 1000 }, frame1080, NoCrop, 'fit');

        expect(layout).toEqual({ width: 1600, height: 900, left: 0, top: 50 });
    });

    it('shows a 2:1 picture letterboxed into a 16:9 file at the full width of a wide window', () => {
        const layout = pictureLayout({ width: 2560, height: 1080 }, frame1080, letterbox, 'fit');

        expect(layout?.height).toBeCloseTo(1080 * 1080 / 960);
        expect(layout?.top).toBeCloseTo(-60 * 1080 / 960);
        expect(layout?.left).toBeCloseTo((2560 - 2160) / 2);
    });

    it('fills a 16:9 window with a 2:1 picture by trimming its sides', () => {
        const layout = pictureLayout(window16x9, frame1080, letterbox, 'fill');

        expect(layout?.height).toBeCloseTo(900 * 1080 / 960);
        expect(layout?.width).toBeCloseTo(1920 * 900 / 960);
        expect(layout?.left).toBeCloseTo((1600 - 1800) / 2);
        expect(layout?.top).toBeCloseTo(-60 * 900 / 960);
    });

    it('fills with a whole frame like object-cover when nothing is known about bars', () => {
        const layout = pictureLayout({ width: 1920, height: 960 }, frame1080, NoCrop, 'fill');

        expect(layout).toEqual({ width: 1920, height: 1080, left: 0, top: -60 });
    });

    it('shows a picture whole rather than cutting more than a fifth of it off to fill', () => {
        const fourByThree = cropOf({ width: 1920, height: 1080, cropTop: 0, cropRight: 240, cropBottom: 0, cropLeft: 240 });
        const scope = cropOf({ width: 1920, height: 1080, cropTop: 138, cropRight: 0, cropBottom: 138, cropLeft: 0 });

        expect(pictureLayout(window16x9, frame1080, fourByThree, 'fill')).toEqual(pictureLayout(window16x9, frame1080, fourByThree, 'fit'));
        expect(pictureLayout(window16x9, frame1080, scope, 'fill')).toEqual(pictureLayout(window16x9, frame1080, scope, 'fit'));
        expect(pictureLayout({ width: 2560, height: 1080 }, frame1080, NoCrop, 'fill')).toEqual(pictureLayout({ width: 2560, height: 1080 }, frame1080, NoCrop, 'fit'));
    });

    it('centres a picture whose bars are uneven', () => {
        const crop = cropOf({ width: 1920, height: 1080, cropTop: 0, cropRight: 0, cropBottom: 0, cropLeft: 240 });

        const layout = pictureLayout({ width: 1680, height: 1080 }, frame1080, crop, 'fit');

        expect(layout?.width).toBeCloseTo(1920);
        expect(layout?.left).toBeCloseTo(-240);
    });

    it('has nothing to lay out before the video says how big it is', () => {
        expect(pictureLayout(window16x9, { width: 0, height: 0 }, NoCrop, 'fit')).toBeNull();
        expect(pictureLayout({ width: 0, height: 900 }, frame1080, NoCrop, 'fit')).toBeNull();
    });

    it('tells fit from fill only when they differ by a pixel or more', () => {
        const fit = pictureLayout(window16x9, frame1080, NoCrop, 'fit');

        expect(sameLayout(fit, pictureLayout(window16x9, frame1080, NoCrop, 'fill'))).toBe(true);
        expect(sameLayout(fit, pictureLayout(window16x9, frame1080, letterbox, 'fill'))).toBe(false);
        expect(sameLayout(null, null)).toBe(true);
        expect(sameLayout(fit, null)).toBe(false);
    });
});

describe('the remembered fit', () => {
    afterEach(() => localStorage.clear());

    it('starts on fill and keeps fit once picked', () => {
        expect(readPictureFit()).toBe('fill');

        savePictureFit('fit');
        expect(readPictureFit()).toBe('fit');

        savePictureFit('fill');
        expect(readPictureFit()).toBe('fill');
    });

    it('reads anything else as fill', () => {
        localStorage.setItem('player_picture_fit', 'zoom');

        expect(readPictureFit()).toBe('fill');
    });
});
