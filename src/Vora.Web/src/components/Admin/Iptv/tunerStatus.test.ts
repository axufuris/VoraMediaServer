import { describe, expect, it } from 'vitest';
import type { HdHomeRunStatusVM } from '../../../api/Iptv/iptvAdminService';
import { describeDevice, describeTunerUse, formatBitrate, formatFrequency, signalTone, tunerChannel } from './tunerStatus';

const status = (patch: Partial<HdHomeRunStatusVM> = {}): HdHomeRunStatusVM => ({
    succeeded: true,
    tunerCount: 4,
    tunersInUse: 1,
    voraStreams: 1,
    tuners: [],
    ...patch,
});

describe('tuner status', () => {
    it('calls a signal good, fair or poor by where it sits', () => {
        expect(signalTone(100)).toBe('good');
        expect(signalTone(80)).toBe('good');
        expect(signalTone(79)).toBe('fair');
        expect(signalTone(50)).toBe('fair');
        expect(signalTone(49)).toBe('poor');
        expect(signalTone(0)).toBe('poor');
    });

    it('writes a frequency without trailing zeros and a bitrate in megabits', () => {
        expect(formatFrequency(473)).toBe('473 MHz');
        expect(formatFrequency(473.125)).toBe('473.125 MHz');
        expect(formatBitrate(19_393_088)).toBe('19.4 Mbps');
    });

    it('names the channel a tuner is on by its number and name, whichever it has', () => {
        expect(tunerChannel({ index: 0, inUse: true, channelNumber: '7.1', channelName: 'WABC-DT' })).toBe('7.1 WABC-DT');
        expect(tunerChannel({ index: 0, inUse: true, channelName: 'KRON' })).toBe('KRON');
        expect(tunerChannel({ index: 0, inUse: true })).toBeNull();
    });

    it('says how many tuners are in use and how many of them Vora holds', () => {
        expect(describeTunerUse(status())).toBe('1 of 4 tuners in use. Vora is streaming 1 channel.');
        expect(describeTunerUse(status({ tunersInUse: 3, voraStreams: 2 }))).toBe('3 of 4 tuners in use. Vora is streaming 2 channels.');
        expect(describeTunerUse(status({ tunerCount: 1, tunersInUse: 0, voraStreams: 0 }))).toBe('0 of 1 tuner in use. Vora is not streaming from it.');
    });

    it('counts the listed tuners when the device did not say how many it has', () => {
        const listed = [{ index: 0, inUse: false }, { index: 1, inUse: false }];

        expect(describeTunerUse(status({ tunerCount: 0, tunersInUse: 0, voraStreams: 0, tuners: listed }))).toBe('0 of 2 tuners in use. Vora is not streaming from it.');
    });

    it('describes the device by what it reported', () => {
        expect(describeDevice(status({ model: 'HDHR5-4K', firmwareVersion: '20240101', deviceId: '1080ABCD' }))).toBe('HDHR5-4K · firmware 20240101 · ID 1080ABCD');
        expect(describeDevice(status())).toBe('');
    });
});
