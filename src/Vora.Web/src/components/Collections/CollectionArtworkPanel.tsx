import type { ReactNode } from 'react';
import type { ArtworkResult } from '../../api/Media/artworkService';
import type { VoraPosterCatalog } from '../../api/Media/voraPosterService';
import type { PluginOptionVM } from '../../api/System/pluginAdminService';
import ArtworkPicker from '../Common/ArtworkPicker';
import VoraPosterPicker from './VoraPosterPicker';
import LockButton from './LockButton';

export type PosterSource = 'artwork' | 'vora';
export type CollectionArtworkKind = 'Poster' | 'Backdrop';

export interface VoraPosterChoices {
    idPrefix: string;
    catalog: VoraPosterCatalog | null;
    failed: boolean;
    suggestedUrl?: string;
    note?: ReactNode;
}

export interface ArtworkFetcher {
    providers: PluginOptionVM[];
    providerId: string;
    onProviderChange: (providerId: string) => void;
    onFetch: () => void;
}

interface CollectionArtworkPanelProps {
    kind: CollectionArtworkKind;
    selectedUrl: string;
    onSelect: (url: string) => void;
    locked: boolean;
    onToggleLock: () => void;
    artwork: ArtworkResult[];
    loading: boolean;
    onUpload: (file: File) => Promise<void>;
    onAddUrl: (url: string) => Promise<void>;
    onDeleteArtwork: (e: React.MouseEvent, artworkId: string) => void | Promise<void>;
    fetcher: ArtworkFetcher;
    posterSource: PosterSource;
    onPosterSourceChange: (source: PosterSource) => void;
    vora: VoraPosterChoices;
}

const POSTER_SOURCES = [['artwork', 'Your artwork'], ['vora', 'Vora posters']] as const;

export default function CollectionArtworkPanel({
    kind, selectedUrl, onSelect, locked, onToggleLock, artwork, loading, onUpload, onAddUrl, onDeleteArtwork,
    fetcher, posterSource, onPosterSourceChange, vora }: CollectionArtworkPanelProps) {
    const isPoster = kind === 'Poster';
    const sourceSwitch = isPoster && (
        <div className="flex flex-wrap items-center gap-2 mb-5" role="group" aria-label="Where the poster comes from">
            {POSTER_SOURCES.map(([source, label]) => (
                <button
                    key={source}
                    type="button"
                    data-active={posterSource === source}
                    aria-pressed={posterSource === source}
                    onClick={() => onPosterSourceChange(source)}
                    className="vora-pill cursor-pointer rounded-full px-4 py-1.5 text-sm font-medium"
                >
                    {label}
                </button>
            ))}
        </div>
    );

    if (isPoster && posterSource === 'vora') {
        return (
            <>
                {sourceSwitch}
                {vora.note && <p className="mb-3 text-xs leading-relaxed text-[var(--vora-text-muted)]">{vora.note}</p>}
                <VoraPosterPicker
                    idPrefix={vora.idPrefix}
                    catalog={vora.catalog}
                    failed={vora.failed}
                    selectedUrl={selectedUrl}
                    suggestedUrl={vora.suggestedUrl}
                    onSelect={onSelect}
                />
            </>
        );
    }

    return (
        <>
            {sourceSwitch}
            <ArtworkPicker
                artType={kind}
                artwork={artwork}
                loading={loading}
                selectedUrl={selectedUrl}
                onSelect={onSelect}
                onUpload={onUpload}
                onAddUrl={onAddUrl}
                onDeleteArtwork={onDeleteArtwork}
                actionRowLeft={
                    <div className="flex items-center gap-2">
                        <select
                            aria-label="Artwork provider"
                            value={fetcher.providerId}
                            onChange={e => fetcher.onProviderChange(e.target.value)}
                            className="p-1.5 bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] text-[var(--vora-text-primary)] text-xs rounded outline-none focus:border-[var(--vora-accent-500)]"
                        >
                            {fetcher.providers.map(p => (
                                <option key={p.id} value={p.id}>{p.name}</option>
                            ))}
                        </select>
                        <button type="button" onClick={fetcher.onFetch} className="px-3 py-1.5 bg-[var(--vora-info-500)] hover:bg-[color-mix(in_srgb,var(--vora-info-500)_85%,var(--vora-text-primary))] active:bg-[color-mix(in_srgb,var(--vora-info-500)_85%,var(--vora-bg-canvas))] text-[var(--vora-text-primary)] text-xs font-bold rounded cursor-pointer transition-colors">
                            Fetch Artwork
                        </button>
                    </div>
                }
                actionRowRight={
                    <div className="flex items-center text-sm text-[var(--vora-text-muted)]">
                        <span className="mr-2">Selected {kind.toLowerCase()}:</span>
                        <LockButton locked={locked} onToggle={onToggleLock} />
                    </div>
                }
            />
        </>
    );
}
