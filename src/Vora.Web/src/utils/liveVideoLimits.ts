import { detectHlsVideoCodecs } from './hardwareScanner';

// What the player tells the server when it starts a live TV channel, so the server copies the
// channel's video when this player plays it and encodes it to H.264 when it can't (an antenna
// channel is usually MPEG-2, which no browser plays). Live TV is always played by hls.js, or the
// browser's own HLS player where Media Source is missing, in the browser and in the desktop app
// alike, so the codecs are the ones the HLS player takes from MPEG-TS segments: never the
// desktop app's native player's list, which the live player doesn't use.
export interface LiveVideoLimits {
    videoCodecs: string[];
}

export const liveVideoLimits = (): LiveVideoLimits => ({ videoCodecs: detectHlsVideoCodecs() });
