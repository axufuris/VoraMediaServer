import { useState, useRef, type RefObject, type UIEvent } from 'react';
import { ROW_HEIGHT } from '../guideConstants';

export interface UseGuideVirtualizationResult {
    scrollTop: number;
    setScrollTop: (n: number) => void;
    scrollContainerRef: RefObject<HTMLDivElement | null>;
    handleScroll: (e: UIEvent<HTMLDivElement>) => void;
    startIndex: number;
    endIndex: number;
    offsetY: number;
    totalHeight: number;
    visibleCount: number;
}

export function useGuideVirtualization(totalChannels: number): UseGuideVirtualizationResult {
    const [scrollTop, setScrollTop] = useState(0);
    const scrollContainerRef = useRef<HTMLDivElement>(null);

    const handleScroll = (e: UIEvent<HTMLDivElement>) => setScrollTop(e.currentTarget.scrollTop);

    const visibleRows = Math.ceil(window.innerHeight / ROW_HEIGHT);
    const startIndex = Math.max(0, Math.floor(scrollTop / ROW_HEIGHT) - 3);
    const endIndex = Math.min(totalChannels, startIndex + visibleRows + 6);
    const offsetY = startIndex * ROW_HEIGHT;
    const totalHeight = totalChannels * ROW_HEIGHT;
    const visibleCount = Math.max(0, endIndex - startIndex);

    return {
        scrollTop,
        setScrollTop,
        scrollContainerRef,
        handleScroll,
        startIndex,
        endIndex,
        offsetY,
        totalHeight,
        visibleCount,
    };
}
