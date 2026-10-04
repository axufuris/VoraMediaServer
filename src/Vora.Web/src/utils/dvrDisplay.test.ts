import { describe, it, expect } from 'vitest';
import { dvrStatusLabel, dvrTabFor, isDvrPlayable, isDvrProcessing } from './dvrDisplay';

describe('DVR statuses', () => {
    it('files every status the server sends under a tab', () => {
        expect(['Pending', 'Recording'].map(dvrTabFor)).toEqual(['Upcoming', 'Upcoming']);
        expect(['Completed', 'PostProcessing', 'DetectingCommercials'].map(dvrTabFor)).toEqual(['Completed', 'Completed', 'Completed']);
        expect(['Failed', 'Conflict', 'Cancelled'].map(dvrTabFor)).toEqual(['Failed', 'Failed', 'Failed']);
    });

    it('only a finished recording can be played while processing ones wait', () => {
        expect(isDvrPlayable('Completed')).toBe(true);
        expect(isDvrPlayable('PostProcessing')).toBe(false);
        expect(isDvrProcessing('DetectingCommercials')).toBe(true);
    });

    it('labels statuses in words', () => {
        expect(dvrStatusLabel('DetectingCommercials')).toBe('Finding commercials');
        expect(dvrStatusLabel('Pending')).toBe('Scheduled');
    });
});
