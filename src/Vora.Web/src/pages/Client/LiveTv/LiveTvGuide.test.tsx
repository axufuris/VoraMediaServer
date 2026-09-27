import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import LiveTvGuide from './LiveTvGuide';
import type { IptvChannelVM } from '../../../api/Iptv/iptvAdminService';
import type { IptvProgramDto } from '../../../api/Iptv/iptvClientService';
import type { UseGuideDataResult } from './hooks/useGuideData';

const START = new Date('2026-09-24T20:00:00Z').getTime();

const channel = (id: string, name: string): IptvChannelVM => ({
    id, playlistId: 'p1', externalChannelId: id, name, streamUrl: `http://x/${id}`, isHiddenByAdmin: false, kind: 'Live',
} as IptvChannelVM);

const program = (channelId: string, startMs: number, endMs: number): IptvProgramDto => ({
    id: `${channelId}-${startMs}`, channelId, title: `${channelId} show`, contentRating: '',
    startTime: new Date(startMs).toISOString(), endTime: new Date(endMs).toISOString(),
});

// Module-level so every render sees the same references, as the real hook's
// state would — a fresh array per render would count as new guide data.
const channels = [channel('alpha', 'Alpha News'), channel('bravo', 'Bravo'), channel('charlie', 'Charlie')];
const guideData: Record<string, IptvProgramDto[]> = {
    alpha: [program('alpha', START - 30 * 60_000, START + 30_000)],
    bravo: [program('bravo', START - 30 * 60_000, START + 2 * 3600_000)],
    charlie: [program('charlie', START - 30 * 60_000, START + 2 * 3600_000)],
};
const guide: UseGuideDataResult = {
    channels,
    guideData,
    recordingSessions: [],
    isLoading: false,
    prefs: { enabledProviders: [], hiddenChannels: [], favoriteChannels: [], regions: [], resolutions: [], hideEmpty: false },
    setPrefs: () => {},
    updatePrefs: () => {},
};

vi.mock('./hooks/useGuideData', () => ({ useGuideData: () => guide }));
vi.mock('../../../contexts/usePlayer', () => ({ usePlayer: () => ({ playMedia: vi.fn() }) }));
vi.mock('../../../dialogs', () => ({
    useDialog: () => ({ alert: () => Promise.resolve(), confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) }),
}));

class NoopResizeObserver {
    observe() {}
    unobserve() {}
    disconnect() {}
}

const channelOrder = () => {
    const names = new Set(channels.map(c => c.name));
    return screen.getAllByRole('heading', { level: 3 }).map(h => h.textContent ?? '').filter(n => names.has(n));
};

describe('LiveTvGuide channel order', () => {
    beforeEach(() => {
        vi.useFakeTimers();
        vi.setSystemTime(START);
        vi.stubGlobal('ResizeObserver', NoopResizeObserver);
    });

    afterEach(() => {
        vi.useRealTimers();
        vi.unstubAllGlobals();
    });

    // Alpha's programme ends 30 s in. Browsing must not see it drop down the
    // list on the next minute tick; it moves when the list is next rebuilt.
    it('does not reorder on the minute tick, only when the list is rebuilt', () => {
        render(<MemoryRouter><LiveTvGuide /></MemoryRouter>);
        expect(channelOrder()).toEqual(['Alpha News', 'Bravo', 'Charlie']);

        act(() => { vi.advanceTimersByTime(60_000); });
        expect(channelOrder()).toEqual(['Alpha News', 'Bravo', 'Charlie']);

        fireEvent.change(screen.getByPlaceholderText('Search channels...'), { target: { value: 'a' } });
        expect(channelOrder()).toEqual(['Bravo', 'Charlie', 'Alpha News']);
    });
});
