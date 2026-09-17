import { type ReactNode, useRef, useState } from 'react';
import type { ArtworkResult } from '../../api/Media/artworkService';

interface ArtworkPickerProps {
    artType: 'Poster' | 'Backdrop';
    artwork: ArtworkResult[];
    loading: boolean;
    selectedUrl: string;
    onSelect: (url: string) => void;
    onUpload: (file: File) => Promise<void>;
    onAddUrl: (url: string) => Promise<void>;
    onDeleteArtwork: (e: React.MouseEvent, artworkId: string) => void | Promise<void>;
    actionRowLeft?: ReactNode;
    actionRowRight?: ReactNode;
}

export default function ArtworkPicker({
    artType,
    artwork,
    loading,
    selectedUrl,
    onSelect,
    onUpload,
    onAddUrl,
    onDeleteArtwork,
    actionRowLeft,
    actionRowRight
}: ArtworkPickerProps) {
    const [customUrl, setCustomUrl] = useState('');
    const fileInputRef = useRef<HTMLInputElement>(null);

    const handleFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file) return;
        try {
            await onUpload(file);
        } finally {
            if (fileInputRef.current) fileInputRef.current.value = '';
        }
    };

    const handleAddUrl = async () => {
        if (!customUrl.trim()) return;
        await onAddUrl(customUrl.trim());
        setCustomUrl('');
    };

    const filtered = artwork.filter(a => a.type === artType);
    const sorted = [...filtered].sort((a, b) => {
        if (a.url === selectedUrl) return -1;
        if (b.url === selectedUrl) return 1;
        return 0;
    });
    if (selectedUrl && !sorted.some(p => p.url === selectedUrl)) {
        sorted.unshift({ id: 'current', url: selectedUrl, type: artType, language: 'Current', isUserUploaded: false });
    }

    const aspectClass = artType === 'Poster' ? 'aspect-[2/3]' : 'aspect-video';
    const gridClass = artType === 'Poster'
        ? 'grid grid-cols-3 sm:grid-cols-4 md:grid-cols-5 gap-4'
        : 'grid grid-cols-2 md:grid-cols-3 gap-4';
    const labelLower = artType.toLowerCase();

    return (
        <div>
            <div className="mb-6 p-4 bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded flex gap-4 items-end">
                <div>
                    <label className="block text-xs font-bold text-[var(--vora-text-muted)] mb-2 uppercase tracking-wide">Upload Local File</label>
                    <input
                        type="file"
                        ref={fileInputRef}
                        onChange={handleFileChange}
                        accept="image/*"
                        className="text-sm text-[var(--vora-text-secondary)] file:mr-4 file:py-2 file:px-4 file:rounded file:border-0 file:text-sm file:font-semibold file:bg-[var(--vora-accent-500)]/20 file:text-[var(--vora-accent-500)] hover:file:bg-[var(--vora-accent-500)]/30 cursor-pointer"
                    />
                </div>
                <div className="flex-1 flex gap-2">
                    <div className="flex-1">
                        <label className="block text-xs font-bold text-[var(--vora-text-muted)] mb-2 uppercase tracking-wide">Or Add From URL</label>
                        <input
                            type="text"
                            value={customUrl}
                            onChange={(e) => setCustomUrl(e.target.value)}
                            placeholder="https://..."
                            className="w-full p-2 bg-[var(--vora-bg-raised)] rounded border border-[var(--vora-border-subtle)] text-sm outline-none focus:border-[var(--vora-accent-500)] text-[var(--vora-text-primary)]"
                        />
                    </div>
                    <button
                        type="button"
                        onClick={handleAddUrl}
                        className="px-4 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-primary)] text-sm font-bold rounded mt-6 transition-colors border border-[var(--vora-border-subtle)] cursor-pointer"
                    >
                        Add
                    </button>
                </div>
            </div>

            {(actionRowLeft || actionRowRight) && (
                <div className="flex justify-between items-center mb-4">
                    <div>{actionRowLeft}</div>
                    <div>{actionRowRight}</div>
                </div>
            )}

            {loading ? (
                <div className="py-20 text-center text-[var(--vora-text-muted)]">Fetching {labelLower}s...</div>
            ) : (
                <div className={gridClass}>
                    {sorted.map((art, idx) => {
                        const isSelected = art.url === selectedUrl;
                        return (
                            <div
                                key={`${art.id}-${idx}`}
                                onClick={() => onSelect(art.url)}
                                className={`${aspectClass} rounded bg-[var(--vora-bg-canvas)] overflow-hidden cursor-pointer relative transition-all group ${isSelected ? 'ring-4 ring-orange-500 scale-95' : 'hover:ring-2 hover:ring-gray-500'}`}
                            >
                                <img src={art.url} alt={`${labelLower} option`} loading="lazy" className="w-full h-full object-cover" />

                                {art.isUserUploaded && (
                                    <>
                                        <span className="absolute top-2 left-2 bg-blue-600 text-[10px] px-1.5 py-0.5 rounded font-bold uppercase text-[var(--vora-text-primary)] shadow">Manual</span>
                                        <button
                                            onClick={(e) => onDeleteArtwork(e, art.id)}
                                            className="absolute top-2 right-2 p-1.5 bg-[var(--vora-danger-500)] hover:bg-[var(--vora-danger-500)] text-[var(--vora-text-primary)] rounded opacity-0 group-hover:opacity-100 transition-opacity shadow cursor-pointer"
                                            aria-label="Delete artwork"
                                        >
                                            <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" /></svg>
                                        </button>
                                    </>
                                )}

                                {isSelected && (
                                    <div className="absolute top-2 right-2 bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)] rounded-full p-1 shadow-xl">
                                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={3} d="M5 13l4 4L19 7" /></svg>
                                    </div>
                                )}
                            </div>
                        );
                    })}
                    {sorted.length === 0 && <div className="col-span-full text-center text-[var(--vora-text-muted)] py-10">No {labelLower}s found.</div>}
                </div>
            )}
        </div>
    );
}
