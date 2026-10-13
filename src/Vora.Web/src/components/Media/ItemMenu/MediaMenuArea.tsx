import type { HTMLAttributes } from 'react';
import type { MediaMenuTarget } from './mediaItemMenu';
import { useMediaMenuTrigger } from './useMediaMenuTrigger';

interface MediaMenuAreaProps extends HTMLAttributes<HTMLDivElement> {
    target: MediaMenuTarget | null | undefined;
}

// A div that opens a title's menu on right-click or a long press, for the places
// a title is drawn as a row or a still instead of a MediaCard. Everything else
// about the div is the caller's. It owns the pointer and context-menu handlers,
// so don't pass those in.
export default function MediaMenuArea({ target, style, ...rest }: MediaMenuAreaProps) {
    const trigger = useMediaMenuTrigger(target);
    return <div {...rest} {...trigger} style={{ ...style, ...trigger?.style }} />;
}
