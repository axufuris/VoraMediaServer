import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import RecommendationsPage from './RecommendationsPage';
import type { GeneratedMixSummaryVM } from '../../api/Music/musicService';

const mocks = vi.hoisted(() => ({
    getProviders: vi.fn(),
    getGlobalRecommendations: vi.fn(),
    getMixes: vi.fn(),
}));

vi.mock('../../api/Discovery/recommendationService', () => ({
    recommendationService: { getProviders: mocks.getProviders, getGlobalRecommendations: mocks.getGlobalRecommendations },
}));
vi.mock('../../api/Music/musicService', () => ({ musicService: { getMixes: mocks.getMixes } }));
vi.mock('../../components/Media/RecommendationRow', () => ({
    default: ({ list }: { list: { title: string } }) => <h2>{list.title}</h2>,
}));

const mix = (id: string, kind: GeneratedMixSummaryVM['kind'], slot: number, name: string): GeneratedMixSummaryVM =>
    ({ id, kind, slot, name, trackCount: 20, generatedAt: '2026-10-27' });

const mixes = [
    mix('daily-1', 'DailyMix', 1, 'Daily Mix 1 · Pop'),
    mix('discover', 'DiscoverMix', 0, 'Discover Weekly'),
    mix('radar', 'ReleaseRadar', 0, 'Release Radar'),
    mix('mood', 'MoodMix', 0, 'Chill'),
];

function MusicPage() {
    const location = useLocation();
    return <p>music page {location.search}</p>;
}

const renderTab = () => render(
    <MemoryRouter initialEntries={['/']}>
        <Routes>
            <Route path="/" element={<RecommendationsPage />} />
            <Route path="/music" element={<MusicPage />} />
        </Routes>
    </MemoryRouter>,
);

beforeEach(() => {
    mocks.getProviders.mockResolvedValue(['openai_recommendations']);
    mocks.getGlobalRecommendations.mockResolvedValue([{ title: 'Because you watched Heat', weight: 1, items: [] }]);
    mocks.getMixes.mockResolvedValue(mixes);
});

afterEach(() => {
    cleanup();
    vi.clearAllMocks();
});

describe('RecommendationsPage music', () => {
    it('lists the made-for-you mixes in a row under the recommendations', async () => {
        renderTab();

        const recommendations = await screen.findByText('Because you watched Heat');
        const heading = await screen.findByText('Made for You');
        expect(recommendations.compareDocumentPosition(heading) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
        expect(screen.getByText('Daily Mix 1 · Pop')).toBeTruthy();
        expect(screen.getByText('Discover Weekly')).toBeTruthy();
        expect(screen.queryByText('Release Radar')).toBeNull();
        expect(screen.queryByText('Chill')).toBeNull();
    });

    it('opens a mix on the Music page', async () => {
        renderTab();

        fireEvent.click(await screen.findByText('Daily Mix 1 · Pop'));

        expect(await screen.findByText('music page ?mix=daily-1')).toBeTruthy();
    });

    it('shows the mixes when no recommendation engine is on', async () => {
        mocks.getProviders.mockResolvedValue([]);
        renderTab();

        expect(await screen.findByText('Made for You')).toBeTruthy();
        expect(screen.queryByText('No recommendation engines enabled')).toBeNull();
    });

    it('says no engine is on when there are no mixes either', async () => {
        mocks.getProviders.mockResolvedValue([]);
        mocks.getMixes.mockRejectedValue(new Error('music is off'));
        renderTab();

        expect(await screen.findByText('No recommendation engines enabled')).toBeTruthy();
    });
});
