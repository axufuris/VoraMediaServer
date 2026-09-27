import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import ProviderAccessList from './ProviderAccessList';
import type { IptvPlaylistVM } from '../../api/Iptv/iptvAdminService';

const provider = (id: string, name: string, kind: 'Tv' | 'Radio'): IptvPlaylistVM => ({
    id, name, isActive: true, supportsWebPlayback: true, maxConcurrentStreams: 0, defaultChannelKind: kind,
});

const providers = [
    provider('us', 'US — IPTV Org', 'Tv'),
    provider('gr', 'Greece — IPTV Org', 'Tv'),
    provider('roku', 'Roku Channel', 'Tv'),
    provider('radio', 'Radio — US Top 100 (Radio Browser)', 'Radio'),
];

describe('provider access list', () => {
    it('lists TV providers under Live TV and radio providers under Radio', () => {
        render(<ProviderAccessList providers={providers} selectedIds={[]} onToggle={vi.fn()} />);

        const liveTv = screen.getByRole('group', { name: 'Live TV' });
        const radio = screen.getByRole('group', { name: 'Radio' });

        expect(within(liveTv).getAllByRole('checkbox')).toHaveLength(3);
        expect(within(liveTv).getByLabelText('Roku Channel')).toBeInTheDocument();
        expect(within(radio).getByLabelText('Radio — US Top 100 (Radio Browser)')).toBeInTheDocument();
    });

    it('leaves out a section with nothing in it', () => {
        render(<ProviderAccessList providers={providers.slice(0, 2)} selectedIds={[]} onToggle={vi.fn()} />);

        expect(screen.queryByRole('group', { name: 'Radio' })).toBeNull();
    });

    it('checks the providers already selected', () => {
        render(<ProviderAccessList providers={providers} selectedIds={['radio']} onToggle={vi.fn()} />);

        expect(screen.getByLabelText('Radio — US Top 100 (Radio Browser)')).toBeChecked();
        expect(screen.getByLabelText('US — IPTV Org')).not.toBeChecked();
    });

    it('toggles a provider by id', () => {
        const onToggle = vi.fn();
        render(<ProviderAccessList providers={providers} selectedIds={[]} onToggle={onToggle} />);

        fireEvent.click(screen.getByLabelText('Greece — IPTV Org'));

        expect(onToggle).toHaveBeenCalledWith('gr');
    });

    it('says when no providers exist', () => {
        render(<ProviderAccessList providers={[]} selectedIds={[]} onToggle={vi.fn()} />);

        expect(screen.getByText('No providers exist yet.')).toBeInTheDocument();
        expect(screen.queryByRole('checkbox')).toBeNull();
    });
});
