import { useState, useEffect, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { libraryAdminService, type UpdateMediaRequest } from '../../api/Media/libraryAdminService';
import { artworkService, type ArtworkResult } from '../../api/Media/artworkService';
import { Modal } from '../Common/Modal';
import ArtworkPicker from '../Common/ArtworkPicker';
import { apiClient } from '../../api/client';
import { useDialog } from '../../dialogs';

interface EditMetadataModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSaved: () => void;
    itemId: string;
    type: 'media' | 'season';
    initialData: UpdateMediaRequest & { lockedFields?: string[] };
}

export default function EditMetadataModal({
    isOpen, onClose, onSaved, itemId, type, initialData }: EditMetadataModalProps) {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const [activeTab, setActiveTab] = useState<'general' | 'poster' | 'backdrop'>('general');
    const [saving, setSaving] = useState(false);
    const [loadingArt, setLoadingArt] = useState(false);
    const [artwork, setArtwork] = useState<ArtworkResult[]>([]);

    const [formData, setFormData] = useState<UpdateMediaRequest>({
        title: '',
        sortTitle: '',
        overview: '',
        contentRating: '',
        releaseDate: '',
        posterUrl: '',
        backgroundUrl: '',
        lockedFields: []
    });

    const fetchArtworkOptions = useCallback(() => {
        setLoadingArt(true);
        artworkService.getArtworkOptions(itemId, serverId)
            .then(setArtwork)
            .catch(console.error)
            .finally(() => setLoadingArt(false));
    }, [itemId, serverId]);

    useEffect(() => {
        if (isOpen && initialData) {
            setFormData({
                title: initialData.title || '',
                sortTitle: initialData.sortTitle || '',
                overview: initialData.overview || '',
                contentRating: initialData.contentRating || '',
                releaseDate: initialData.releaseDate ? initialData.releaseDate.split('T')[0] : '',
                posterUrl: initialData.posterUrl || '',
                backgroundUrl: initialData.backgroundUrl || '',
                lockedFields: initialData.lockedFields || []
            });
            setActiveTab('general');
        }
    }, [isOpen, initialData]);

    useEffect(() => {
        if (isOpen && type === 'media' && (activeTab === 'poster' || activeTab === 'backdrop') && artwork.length === 0) {
            fetchArtworkOptions();
        }
    }, [isOpen, activeTab, type, artwork.length, fetchArtworkOptions]);


    const handleChange = (field: keyof UpdateMediaRequest, value: UpdateMediaRequest[keyof UpdateMediaRequest]) => {
        setFormData(prev => ({ ...prev, [field]: value }));
    };

    const handleLockToggle = (field: string) => {
        setFormData(prev => {
            const isLocked = prev.lockedFields.includes(field);
            return {
                ...prev,
                lockedFields: isLocked
                    ? prev.lockedFields.filter(f => f !== field)
                    : [...prev.lockedFields, field]
            };
        });
    };

    const handleSelectArt = (url: string, artType: 'PosterUrl' | 'BackgroundUrl') => {
        setFormData(prev => ({
            ...prev,
            [artType === 'PosterUrl' ? 'posterUrl' : 'backgroundUrl']: url,
            lockedFields: prev.lockedFields.includes(artType) ? prev.lockedFields : [...prev.lockedFields, artType]
        }));
    };

    const uploadArtwork = async (artType: 'Poster' | 'Backdrop', file: File) => {
        const data = new FormData();
        data.append('file', file);
        try {
            await apiClient.post(`/media/${itemId}/artwork/upload?type=${artType}`, data, {
                headers: { 'Content-Type': 'multipart/form-data' },
                serverId
            });
            fetchArtworkOptions();
        } catch (err) {
            await dialog.alert("Upload failed");
            console.error(err);
        }
    };

    const addArtworkUrl = async (artType: 'Poster' | 'Backdrop', url: string) => {
        try {
            await apiClient.post(`/media/${itemId}/artwork/url?type=${artType}`, `"${url}"`, {
                headers: { 'Content-Type': 'application/json' },
                serverId
            });
            fetchArtworkOptions();
        } catch (err) {
            await dialog.alert("Failed to add URL");
            console.error(err);
        }
    };

    const deleteArtwork = async (e: React.MouseEvent, artworkId: string) => {
        e.stopPropagation();
        if (!await dialog.confirm("Delete this custom artwork?")) return;
        try {
            await apiClient.delete(`/media/artwork/${artworkId}`, { serverId });
            fetchArtworkOptions();
        } catch (err) {
            await dialog.alert("Failed to delete artwork");
            console.error(err);
        }
    };

    const handleSubmit = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        setSaving(true);
        try {
            if (type === 'media') {
                await libraryAdminService.updateMediaItem(itemId, formData, serverId);
            } else {
                await libraryAdminService.updateSeason(itemId, formData, serverId);
            }
            onSaved();
            onClose();
        } catch (err) {
            console.error(err);
            await dialog.alert("Failed to save metadata.");
        } finally {
            setSaving(false);
        }
    };

    return (
        <Modal
            isOpen={isOpen}
            onClose={onClose}
            size="4xl"
            surface="gray-900"
            cardClassName="max-h-[90vh] flex flex-col overflow-hidden"
        >

                <div className="px-6 pt-6 border-b border-[var(--vora-border-subtle)] bg-[var(--vora-bg-canvas)]">
                    <div className="flex justify-between items-center mb-6">
                        <h2 className="text-2xl font-bold text-[var(--vora-text-primary)]">Edit {type === 'media' ? 'Metadata' : 'Season'}</h2>
                        <button onClick={onClose} className="text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] cursor-pointer">
                            <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" /></svg>
                        </button>
                    </div>

                    <div className="flex gap-6 text-sm font-bold text-[var(--vora-text-muted)]">
                        <button onClick={() => setActiveTab('general')} className={`pb-3 border-b-2 transition-colors cursor-pointer ${activeTab === 'general' ? 'border-orange-500 text-[var(--vora-accent-500)]' : 'border-transparent hover:text-[var(--vora-text-primary)]'}`}>
                            General
                        </button>
                        {type === 'media' && (
                            <>
                                <button onClick={() => setActiveTab('poster')} className={`pb-3 border-b-2 transition-colors cursor-pointer ${activeTab === 'poster' ? 'border-orange-500 text-[var(--vora-accent-500)]' : 'border-transparent hover:text-[var(--vora-text-primary)]'}`}>
                                    Posters
                                </button>
                                <button onClick={() => setActiveTab('backdrop')} className={`pb-3 border-b-2 transition-colors cursor-pointer ${activeTab === 'backdrop' ? 'border-orange-500 text-[var(--vora-accent-500)]' : 'border-transparent hover:text-[var(--vora-text-primary)]'}`}>
                                    Backdrops
                                </button>
                            </>
                        )}
                    </div>
                </div>

                <div className="flex-1 overflow-y-auto p-6 custom-scrollbar bg-[var(--vora-bg-raised)]">
                    {activeTab === 'general' && (
                        <form id="metadata-form" onSubmit={handleSubmit} className="space-y-5">
                            <div>
                                <div className="flex justify-between items-center mb-1">
                                    <label className="text-sm font-semibold text-[var(--vora-text-secondary)]">Title</label>
                                    <button type="button" onClick={() => handleLockToggle('Title')} className="cursor-pointer">
                                        <LockIcon locked={formData.lockedFields.includes('Title')} />
                                    </button>
                                </div>
                                <input type="text" value={formData.title} onChange={e => handleChange('title', e.target.value)} className="w-full p-2.5 bg-[var(--vora-bg-canvas)] rounded border border-[var(--vora-border-subtle)] text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)] transition-colors" />
                            </div>

                            <div>
                                <div className="flex justify-between items-center mb-1">
                                    <label className="text-sm font-semibold text-[var(--vora-text-secondary)]">Sort Title</label>
                                    <button type="button" onClick={() => handleLockToggle('SortTitle')} className="cursor-pointer">
                                        <LockIcon locked={formData.lockedFields.includes('SortTitle')} />
                                    </button>
                                </div>
                                <input type="text" value={formData.sortTitle || ''} onChange={e => handleChange('sortTitle', e.target.value)} className="w-full p-2.5 bg-[var(--vora-bg-canvas)] rounded border border-[var(--vora-border-subtle)] text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)] transition-colors" />
                            </div>

                            <div>
                                <div className="flex justify-between items-center mb-1">
                                    <label className="text-sm font-semibold text-[var(--vora-text-secondary)]">Overview</label>
                                    <button type="button" onClick={() => handleLockToggle('Overview')} className="cursor-pointer">
                                        <LockIcon locked={formData.lockedFields.includes('Overview')} />
                                    </button>
                                </div>
                                <textarea rows={5} value={formData.overview || ''} onChange={e => handleChange('overview', e.target.value)} className="w-full p-2.5 bg-[var(--vora-bg-canvas)] rounded border border-[var(--vora-border-subtle)] text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)] transition-colors resize-none" />
                            </div>

                            <div className="grid grid-cols-2 gap-6">
                                <div>
                                    <div className="flex justify-between items-center mb-1">
                                        <label className="text-sm font-semibold text-[var(--vora-text-secondary)]">Release Date</label>
                                        <button type="button" onClick={() => handleLockToggle('ReleaseDate')} className="cursor-pointer">
                                            <LockIcon locked={formData.lockedFields.includes('ReleaseDate')} />
                                        </button>
                                    </div>
                                    <input type="date" value={formData.releaseDate || ''} onChange={e => handleChange('releaseDate', e.target.value)} className="w-full p-2.5 bg-[var(--vora-bg-canvas)] rounded border border-[var(--vora-border-subtle)] text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)] transition-colors color-scheme-dark" />
                                </div>

                                <div>
                                    <div className="flex justify-between items-center mb-1">
                                        <label className="text-sm font-semibold text-[var(--vora-text-secondary)]">Content Rating</label>
                                        <button type="button" onClick={() => handleLockToggle('ContentRating')} className="cursor-pointer">
                                            <LockIcon locked={formData.lockedFields.includes('ContentRating')} />
                                        </button>
                                    </div>
                                    <input type="text" value={formData.contentRating || ''} onChange={e => handleChange('contentRating', e.target.value)} className="w-full p-2.5 bg-[var(--vora-bg-canvas)] rounded border border-[var(--vora-border-subtle)] text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)] transition-colors" />
                                </div>
                            </div>
                        </form>
                    )}

                    {activeTab === 'poster' && (
                        <ArtworkPicker
                            artType="Poster"
                            artwork={artwork}
                            loading={loadingArt}
                            selectedUrl={formData.posterUrl || ''}
                            onSelect={(url) => handleSelectArt(url, 'PosterUrl')}
                            onUpload={(file) => uploadArtwork('Poster', file)}
                            onAddUrl={(url) => addArtworkUrl('Poster', url)}
                            onDeleteArtwork={deleteArtwork}
                            actionRowLeft={<p className="text-sm text-[var(--vora-text-muted)]">Select a poster to apply it immediately to the form.</p>}
                            actionRowRight={
                                <button type="button" onClick={() => handleLockToggle('PosterUrl')} className="flex items-center gap-2 text-sm text-[var(--vora-text-secondary)] cursor-pointer">
                                    <LockIcon locked={formData.lockedFields.includes('PosterUrl')} /> Lock Poster
                                </button>
                            }
                        />
                    )}

                    {activeTab === 'backdrop' && (
                        <ArtworkPicker
                            artType="Backdrop"
                            artwork={artwork}
                            loading={loadingArt}
                            selectedUrl={formData.backgroundUrl || ''}
                            onSelect={(url) => handleSelectArt(url, 'BackgroundUrl')}
                            onUpload={(file) => uploadArtwork('Backdrop', file)}
                            onAddUrl={(url) => addArtworkUrl('Backdrop', url)}
                            onDeleteArtwork={deleteArtwork}
                            actionRowLeft={<p className="text-sm text-[var(--vora-text-muted)]">Select a backdrop to apply it immediately to the form.</p>}
                            actionRowRight={
                                <button type="button" onClick={() => handleLockToggle('BackgroundUrl')} className="flex items-center gap-2 text-sm text-[var(--vora-text-secondary)] cursor-pointer">
                                    <LockIcon locked={formData.lockedFields.includes('BackgroundUrl')} /> Lock Backdrop
                                </button>
                            }
                        />
                    )}
                </div>

                <div className="px-6 py-4 bg-[var(--vora-bg-canvas)] border-t border-[var(--vora-border-subtle)] flex justify-end gap-3">
                    <button onClick={onClose} disabled={saving} className="px-5 py-2 text-sm font-bold text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] transition-colors cursor-pointer">
                        Cancel
                    </button>
                    <button type="submit" form="metadata-form" onClick={activeTab !== 'general' ? handleSubmit : undefined} disabled={saving} className="px-6 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-hover)] active:bg-[var(--vora-accent-hover)] disabled:opacity-50 text-[var(--vora-text-primary)] text-sm font-bold rounded shadow transition-colors cursor-pointer">
                        {saving ? 'Saving...' : 'Save Changes'}
                    </button>
                </div>
        </Modal>
    );
}

const LockIcon = ({ locked }: { locked: boolean }) => (
    <div className={`p-1.5 rounded transition-colors ${locked ? 'bg-[var(--vora-accent-500)]/20 text-[var(--vora-accent-500)]' : 'bg-[var(--vora-bg-sunken)] text-[var(--vora-text-muted)] hover:text-[var(--vora-text-secondary)]'}`}>
        {locked ? (
            <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 20 20"><path fillRule="evenodd" d="M5 9V7a5 5 0 0110 0v2a2 2 0 012 2v5a2 2 0 01-2 2H5a2 2 0 01-2-2v-5a2 2 0 012-2zm8-2v2H7V7a3 3 0 016 0z" clipRule="evenodd" /></svg>
        ) : (
            <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 20 20"><path d="M10 2a5 5 0 00-5 5v2a2 2 0 00-2 2v5a2 2 0 002 2h10a2 2 0 002-2v-5a2 2 0 00-2-2H7V7a3 3 0 015.905-.75 1 1 0 001.937-.5A5.002 5.002 0 0010 2z" /></svg>
        )}
    </div>
);