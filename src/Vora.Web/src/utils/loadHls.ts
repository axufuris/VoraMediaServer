import type Hls from 'hls.js';

// Memoized dynamic import. The first caller triggers the chunk fetch;
// subsequent callers reuse the same in-flight or resolved promise so
// hls.js never loads twice in a session.
let hlsPromise: Promise<typeof Hls> | null = null;

export const loadHls = (): Promise<typeof Hls> => {
    if (!hlsPromise) {
        hlsPromise = import('hls.js').then(m => m.default);
    }
    return hlsPromise;
};
