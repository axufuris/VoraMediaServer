import { describe, it, expect, afterEach, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import type { MediaExtra } from '../../api/Media/mediaService';
import MediaExtrasRow from './MediaExtrasRow';

vi.mock('../../contexts/usePlayer', () => ({ usePlayer: () => ({ playMedia: vi.fn() }) }));
vi.mock('../../api/Streaming/streamingService', () => ({ streamingService: { startExtraSession: vi.fn() } }));

const teaser: MediaExtra = { id: 'extra-1', title: 'Teaser', extraType: 'Trailer', thumbnailUrl: '/api/artwork/extra/extra-1' };
const makingOf: MediaExtra = { id: 'extra-2', title: 'Making Of', extraType: 'BehindTheScenes', thumbnailUrl: '/api/artwork/extra/extra-2' };

describe('MediaExtrasRow', () => {
    afterEach(cleanup);

    it('shows each local extra with the thumbnail the server made for it', () => {
        render(<MediaExtrasRow videos={[]} extras={[teaser, makingOf]} />);

        expect(screen.getByAltText('Teaser')).toHaveAttribute('src', '/api/artwork/extra/extra-1');
        expect(screen.getByAltText('Making Of')).toHaveAttribute('src', '/api/artwork/extra/extra-2');
    });

    it('keeps the placeholder for an extra the server sent no thumbnail for', () => {
        render(<MediaExtrasRow videos={[]} extras={[{ ...teaser, thumbnailUrl: undefined }]} />);

        expect(screen.getByText('Teaser')).toBeInTheDocument();
        expect(screen.queryByRole('img')).not.toBeInTheDocument();
    });
});
