import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ScrubThumbnail } from './VideoScrubThumbnails';

const bar = { left: 100, top: 900, width: 1000, height: 6, right: 1100, bottom: 906, x: 100, y: 900, toJSON: () => ({}) } as DOMRect;

describe('ScrubThumbnail', () => {
    it('shows the time on the thumbnail itself, clear of the seek bar', () => {
        const { container } = render(
            <ScrubThumbnail hoverPercent={0.5} duration={8700} barRect={bar} cue={{ start: 4340, end: 4360, x: 160, y: 90, width: 160, height: 90 }} spriteUrl="/sprite.jpg" width={160} height={90} />,
        );

        const time = screen.getByTestId('scrub-thumbnail-time');
        expect(time).toHaveTextContent('1:12:30');
        const wrapper = container.firstElementChild as HTMLElement;
        expect(wrapper.style.height).toBe('90px');
        expect(Number.parseFloat(wrapper.style.top) + 90).toBeLessThan(bar.top);
        expect(time.parentElement?.style.backgroundImage).toContain('/sprite.jpg');
    });

    it('renders nothing while the pointer is off the bar', () => {
        const { container } = render(
            <ScrubThumbnail hoverPercent={null} duration={8700} barRect={bar} cue={null} spriteUrl="/sprite.jpg" width={160} height={90} />,
        );

        expect(container).toBeEmptyDOMElement();
    });
});
