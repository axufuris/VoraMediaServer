import type { HdHomeRunStatusVM, HdHomeRunTunerVM } from '../../../api/Iptv/iptvAdminService';

export type SignalTone = 'good' | 'fair' | 'poor';

const GOOD_SIGNAL_PERCENT = 80;
const FAIR_SIGNAL_PERCENT = 50;
const BITS_PER_MEGABIT = 1_000_000;

export function signalTone(percent: number): SignalTone {
    if (percent >= GOOD_SIGNAL_PERCENT) return 'good';
    if (percent >= FAIR_SIGNAL_PERCENT) return 'fair';
    return 'poor';
}

export function formatFrequency(megahertz: number): string {
    return `${Number(megahertz.toFixed(3))} MHz`;
}

export function formatBitrate(bitsPerSecond: number): string {
    return `${(bitsPerSecond / BITS_PER_MEGABIT).toFixed(1)} Mbps`;
}

export function tunerChannel(tuner: HdHomeRunTunerVM): string | null {
    const parts = [tuner.channelNumber, tuner.channelName].filter((part): part is string => Boolean(part));
    return parts.length > 0 ? parts.join(' ') : null;
}

export function describeTunerUse(status: HdHomeRunStatusVM): string {
    const total = status.tunerCount > 0 ? status.tunerCount : status.tuners.length;
    const used = `${status.tunersInUse} of ${total} ${total === 1 ? 'tuner' : 'tuners'} in use`;
    if (status.voraStreams === 0) return `${used}. Vora is not streaming from it.`;
    return `${used}. Vora is streaming ${status.voraStreams} ${status.voraStreams === 1 ? 'channel' : 'channels'}.`;
}

export function describeDevice(status: HdHomeRunStatusVM): string {
    const parts = [status.model, status.firmwareVersion ? `firmware ${status.firmwareVersion}` : null, status.deviceId ? `ID ${status.deviceId}` : null];
    return parts.filter((part): part is string => Boolean(part)).join(' · ');
}
