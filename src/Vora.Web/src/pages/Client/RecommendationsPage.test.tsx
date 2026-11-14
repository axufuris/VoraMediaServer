import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import RecommendationsPage from './RecommendationsPage';
import type { GeneratedMixSummaryVM } from '../../api/Music/musicService';
import type { NavItem } from '../../layouts/MainLayout';
import { SidebarItemsContext, type SidebarItems } from '../../layouts/sidebarItems';

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

describe('RecommendationsPage and the sidebar', () => {
    const library = (id: string, isPinned: boolean): NavItem =>
        ({ id, serverId: 'home', serverName: 'Home', title: id, path: `/server/home/library/${id}`, type: 'library', isPinned, order: 0 });

    const sidebar = (items: NavItem[], loaded = true): SidebarItems => ({ items, loaded });

    const tab = (value: SidebarItems) => (
        <MemoryRouter initialEntries={['/server/home']}>
            <SidebarItemsContext.Provider value={value}>
                <Routes>
                    <Route path="/server/:serverId" element={<RecommendationsPage />} />
                </Routes>
            </SidebarItemsContext.Provider>
        </MemoryRouter>
    );

    it('waits for the sidebar, then asks without the libraries the profile unpinned', async () => {
        const items = [library('movies', true), library('test-movies', false), library('test-shows', false)];
        const view = render(tab(sidebar(items, false)));

        await waitFor(() => expect(mocks.getProviders).toHaveBeenCalled());
        await Promise.resolve();
        expect(mocks.getGlobalRecommendations).not.toHaveBeenCalled();

        view.rerender(tab(sidebar(items)));

        expect(await screen.findByText('Because you watched Heat')).toBeTruthy();
        expect(mocks.getGlobalRecommendations).toHaveBeenCalledTimes(1);
        expect(mocks.getGlobalRecommendations).toHaveBeenCalledWith('openai_recommendations', 'home', ['test-movies', 'test-shows']);
    });

    it('asks again when a library is unpinned, without reloading the page', async () => {
        const view = render(tab(sidebar([library('movies', true), library('test-shows', true)])));
        expect(await screen.findByText('Because you watched Heat')).toBeTruthy();
        expect(mocks.getGlobalRecommendations).toHaveBeenLastCalledWith('openai_recommendations', 'home', []);

        mocks.getGlobalRecommendations.mockResolvedValue([{ title: "Top rated you haven't seen", weight: 1, items: [] }]);
        view.rerender(tab(sidebar([library('movies', true), library('test-shows', false)])));

        expect(await screen.findByText("Top rated you haven't seen")).toBeTruthy();
        expect(mocks.getGlobalRecommendations).toHaveBeenLastCalledWith('openai_recommendations', 'home', ['test-shows']);
        expect(mocks.getProviders).toHaveBeenCalledTimes(1);
    });
});
