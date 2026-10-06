import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { SCRUB_TIME_GAP, SCRUB_TIME_HEIGHT, ScrubPreview } from './VideoScrubThumbnails';

const bar = { left: 100, top: 900, width: 1000, height: 6, right: 1100, bottom: 906, x: 100, y: 900, toJSON: () => ({}) } as DOMRect;
const cue = { start: 4340, end: 4360, x: 160, y: 90, width: 160, height: 90 };
const top = (el: HTMLElement) => Number.parseFloat(el.style.top);

describe('ScrubPreview', () => {
    it('shows the time just above the bar and the thumbnail above the time', () => {
        render(<ScrubPreview hoverPercent={0.5} duration={8700} barRect={bar} cue={cue} spriteUrl="/sprite.jpg" width={160} height={90} />);

        const time = screen.getByTestId('scrub-time');
        const thumbnail = screen.getByTestId('scrub-thumbnail');
        expect(time).toHaveTextContent('1:12:30');
        expect(top(time) + SCRUB_TIME_HEIGHT + SCRUB_TIME_GAP).toBe(bar.top);
        expect(top(thumbnail) + 90).toBeLessThan(top(time));
        expect(thumbnail.style.backgroundImage).toContain('/sprite.jpg');
    });

    it('still shows the time above the bar when the video has no thumbnails', () => {
        render(<ScrubPreview hoverPercent={0.25} duration={8700} barRect={bar} cue={null} spriteUrl="" width={0} height={0} />);

        const time = screen.getByTestId('scrub-time');
        expect(time).toHaveTextContent('36:15');
        expect(top(time) + SCRUB_TIME_HEIGHT + SCRUB_TIME_GAP).toBe(bar.top);
        expect(screen.queryByTestId('scrub-thumbnail')).toBeNull();
    });

    it('renders nothing while the pointer is off the bar', () => {
        const { container } = render(<ScrubPreview hoverPercent={null} duration={8700} barRect={bar} cue={cue} spriteUrl="/sprite.jpg" width={160} height={90} />);

        expect(container).toBeEmptyDOMElement();
    });
});
