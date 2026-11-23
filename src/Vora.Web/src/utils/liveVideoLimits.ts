import { detectHlsVideoCodecs } from './hardwareScanner';

// What the player tells the server when it starts a live TV channel, so the server copies the
// channel's video when this player plays it and encodes it to H.264 when it can't (an antenna
// channel is usually MPEG-2, which no browser plays). Live TV is always played by hls.js, or the
// browser's own HLS player where Media Source is missing, in the browser and in the desktop app
// alike, so the codecs are the ones the HLS player takes from MPEG-TS segments: never the
// desktop app's native player's list, which the live player doesn't use.
//
// maxHeight is the tallest picture the screen can use, which sizes the encode (a phone gets 720p,
// which costs the server a third less CPU than 1080p and looks the same on a small screen). It is
// left out when the browser doesn't say, and the server then encodes up to 1080.
export interface LiveVideoLimits {
    videoCodecs: string[];
    maxHeight?: number;
}

const PHONE_SCREEN_CSS_PIXELS = 600;
const PHONE_VIDEO_HEIGHT = 720;

// The tallest video, in pixels, the screen shows: a phone-sized screen (its short side under 600 CSS
// pixels) asks for 720; any other screen for its own short side in device pixels, which the server
// snaps to 480, 720 or 1080.
export const screenVideoHeight = (screen: { width: number; height: number }, pixelRatio: number): number | undefined => {
    const shortSide = Math.min(screen.width, screen.height);
    if (!(shortSide > 0)) return undefined;
    if (shortSide < PHONE_SCREEN_CSS_PIXELS) return PHONE_VIDEO_HEIGHT;
    return Math.round(shortSide * (pixelRatio > 0 ? pixelRatio : 1));
};

export const liveVideoLimits = (): LiveVideoLimits => {
    const maxHeight = screenVideoHeight(window.screen, window.devicePixelRatio);
    return { videoCodecs: detectHlsVideoCodecs(), ...(maxHeight ? { maxHeight } : {}) };
};
