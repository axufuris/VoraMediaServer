import { useState, useEffect, useRef } from 'react';
import { useParams } from 'react-router-dom';
import { musicService, type ArtistVM, type AlbumVM, type TrackVM, type MusicArtworkResultVM } from '../../api/Music/musicService';
import { Modal, ModalHeader } from '../Common/Modal';
import { useDialog } from '../../dialogs';

export type MusicEntityKind = 'artist' | 'album' | 'track';
type MusicEditTab = 'details' | 'image';

interface MusicMetadataEditModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSaved: () => void;
    kind: MusicEntityKind;
    artist?: ArtistVM | null;
    album?: AlbumVM | null;
    track?: TrackVM | null;
}

const RATING_QUICK_PICKS = ['Explicit', 'Clean'];

export default function MusicMetadataEditModal({ isOpen, onClose, onSaved, kind, artist, album, track }: MusicMetadataEditModalProps) {
    const { serverId } = useParams<{ serverId?: string }>();
    const dialog = useDialog();
    const [saving, setSaving] = useState(false);
    const [lockedFields, setLockedFields] = useState<string[]>([]);
    const [activeTab, setActiveTab] = useState<MusicEditTab>('details');

    const hasImageTab = kind === 'artist' || kind === 'album';

    const [name, setName] = useState('');
    const [sortName, setSortName] = useState('');
    const [biography, setBiography] = useState('');
    const [artworkUrl, setArtworkUrl] = useState('');
    const [backgroundUrl, setBackgroundUrl] = useState('');
    const [bannerUrl, setBannerUrl] = useState('');
    const [clearLogoUrl, setClearLogoUrl] = useState('');
    const [discArtUrl, setDiscArtUrl] = useState('');

    const [title, setTitle] = useState('');
    const [sortTitle, setSortTitle] = useState('');
    const [year, setYear] = useState<string>('');
    const [genre, setGenre] = useState('');

    const [trackNumber, setTrackNumber] = useState<string>('');
    const [discNumber, setDiscNumber] = useState<string>('');
    const [contentRating, setContentRating] = useState('');

    const [uploading, setUploading] = useState(false);
    const [uploadingBackground, setUploadingBackground] = useState(false);
    const [uploadingBanner, setUploadingBanner] = useState(false);
    const [uploadingClearLogo, setUploadingClearLogo] = useState(false);
    const [uploadingDiscArt, setUploadingDiscArt] = useState(false);
    const fileInputRef = useRef<HTMLInputElement>(null);
    const backgroundFileInputRef = useRef<HTMLInputElement>(null);
    const bannerFileInputRef = useRef<HTMLInputElement>(null);
    const clearLogoFileInputRef = useRef<HTMLInputElement>(null);
    const discArtFileInputRef = useRef<HTMLInputElement>(null);

    useEffect(() => {
        if (!isOpen) return;
        setActiveTab('details');
        if (kind === 'artist' && artist) {
            setName(artist.name);
            setSortName(artist.sortName || '');
            setBiography(artist.biography || '');
            setArtworkUrl(artist.artworkUrl || '');
            setBackgroundUrl(artist.backgroundUrl || '');
            setBannerUrl(artist.bannerUrl || '');
            setClearLogoUrl(artist.clearLogoUrl || '');
            setLockedFields([...(artist.lockedFields || [])]);
        } else if (kind === 'album' && album) {
            setTitle(album.title);
            setSortTitle(album.sortTitle || '');
            setYear(album.year != null ? album.year.toString() : '');
            setGenre(album.genre || '');
            setArtworkUrl(album.artworkUrl || '');
            setBackgroundUrl(album.backgroundUrl || '');
            setDiscArtUrl(album.discArtUrl || '');
            setLockedFields([...(album.lockedFields || [])]);
        } else if (kind === 'track' && track) {
            setTitle(track.title);
            setSortTitle(track.sortTitle || '');
            setTrackNumber(track.trackNumber.toString());
            setDiscNumber(track.discNumber != null ? track.discNumber.toString() : '');
            setContentRating(track.contentRating || '');
            setLockedFields([...(track.lockedFields || [])]);
        }
    }, [isOpen, kind, artist, album, track]);

    const toggleLock = (field: string) => {
        setLockedFields(prev => prev.includes(field) ? prev.filter(f => f !== field) : [...prev, field]);
    };

    const isLocked = (field: string) => lockedFields.includes(field);

    const handleArtworkUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file) return;

        setUploading(true);
        try {
            let url: string;
            if (kind === 'artist' && artist) {
                url = await musicService.uploadArtistArtwork(artist.id, file, serverId);
            } else if (kind === 'album' && album) {
                url = await musicService.uploadAlbumArtwork(album.id, file, serverId);
            } else {
                return;
            }
            setArtworkUrl(url);
            if (!lockedFields.includes('ArtworkUrl')) {
                setLockedFields(prev => [...prev, 'ArtworkUrl']);
            }
        } catch (err) {
            console.error('Failed to upload artwork', err);
            await dialog.alert('Upload failed.');
        } finally {
            setUploading(false);
            if (fileInputRef.current) fileInputRef.current.value = '';
        }
    };

    const handleBackgroundUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file) return;

        setUploadingBackground(true);
        try {
            let url: string;
            if (kind === 'artist' && artist) {
                url = await musicService.uploadArtistBackground(artist.id, file, serverId);
            } else if (kind === 'album' && album) {
                url = await musicService.uploadAlbumBackground(album.id, file, serverId);
            } else {
                return;
            }
            setBackgroundUrl(url);
            if (!lockedFields.includes('BackgroundUrl')) {
                setLockedFields(prev => [...prev, 'BackgroundUrl']);
            }
        } catch (err) {
            console.error('Failed to upload background', err);
            await dialog.alert('Upload failed.');
        } finally {
            setUploadingBackground(false);
            if (backgroundFileInputRef.current) backgroundFileInputRef.current.value = '';
        }
    };

    const handleBannerUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file || kind !== 'artist' || !artist) return;

        setUploadingBanner(true);
        try {
            const url = await musicService.uploadArtistBanner(artist.id, file, serverId);
            setBannerUrl(url);
            if (!lockedFields.includes('BannerUrl')) {
                setLockedFields(prev => [...prev, 'BannerUrl']);
            }
        } catch (err) {
            console.error('Failed to upload banner', err);
            await dialog.alert('Upload failed.');
        } finally {
            setUploadingBanner(false);
            if (bannerFileInputRef.current) bannerFileInputRef.current.value = '';
        }
    };

    const handleClearLogoUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file || kind !== 'artist' || !artist) return;

        setUploadingClearLogo(true);
        try {
            const url = await musicService.uploadArtistClearLogo(artist.id, file, serverId);
            setClearLogoUrl(url);
            if (!lockedFields.includes('ClearLogoUrl')) {
                setLockedFields(prev => [...prev, 'ClearLogoUrl']);
            }
        } catch (err) {
            console.error('Failed to upload clear logo', err);
            await dialog.alert('Upload failed.');
        } finally {
            setUploadingClearLogo(false);
            if (clearLogoFileInputRef.current) clearLogoFileInputRef.current.value = '';
        }
    };

    const handleDiscArtUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file || kind !== 'album' || !album) return;

        setUploadingDiscArt(true);
        try {
            const url = await musicService.uploadAlbumDiscArt(album.id, file, serverId);
            setDiscArtUrl(url);
            if (!lockedFields.includes('DiscArtUrl')) {
                setLockedFields(prev => [...prev, 'DiscArtUrl']);
            }
        } catch (err) {
            console.error('Failed to upload disc art', err);
            await dialog.alert('Upload failed.');
        } finally {
            setUploadingDiscArt(false);
            if (discArtFileInputRef.current) discArtFileInputRef.current.value = '';
        }
    };

    const handleSave = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        setSaving(true);
        try {
            if (kind === 'artist' && artist) {
                await musicService.updateArtist(artist.id, {
                    name,
                    sortName: sortName.trim() || null,
                    biography: biography.trim() || null,
                    artworkUrl: artworkUrl.trim() || null,
                    backgroundUrl: backgroundUrl.trim() || null,
                    bannerUrl: bannerUrl.trim() || null,
                    clearLogoUrl: clearLogoUrl.trim() || null,
                    lockedFields
                }, serverId);
            } else if (kind === 'album' && album) {
                await musicService.updateAlbum(album.id, {
                    title,
                    sortTitle: sortTitle.trim() || null,
                    year: year.trim() ? parseInt(year, 10) : null,
                    genre: genre.trim() || null,
                    artworkUrl: artworkUrl.trim() || null,
                    backgroundUrl: backgroundUrl.trim() || null,
                    discArtUrl: discArtUrl.trim() || null,
                    lockedFields
                }, serverId);
            } else if (kind === 'track' && track) {
                await musicService.updateTrack(track.id, {
                    title,
                    sortTitle: sortTitle.trim() || null,
                    trackNumber: parseInt(trackNumber, 10) || 0,
                    discNumber: discNumber.trim() ? parseInt(discNumber, 10) : null,
                    contentRating: contentRating.trim() || null,
                    lockedFields
                }, serverId);
            }
            onSaved();
            onClose();
        } catch (err) {
            console.error('Failed to save metadata', err);
            await dialog.alert('Failed to save changes.');
        } finally {
            setSaving(false);
        }
    };

    const title_modal = kind === 'artist' ? `Edit Artist: ${artist?.name || ''}`
        : kind === 'album' ? `Edit Album: ${album?.title || ''}`
        : `Edit Track: ${track?.title || ''}`;

    const FieldLabel = ({ field, label }: { field: string; label: string }) => (
        <div className="flex items-center justify-between mb-1">
            <label className="text-sm font-medium text-gray-400">{label}</label>
            <button
                type="button"
                onClick={() => toggleLock(field)}
                className={`text-xs px-2 py-0.5 rounded transition-colors cursor-pointer ${isLocked(field) ? 'bg-orange-600/30 text-orange-400 hover:bg-orange-600/40' : 'bg-gray-800 text-gray-500 hover:text-gray-300'}`}
                title={isLocked(field) ? 'Field is locked — won\'t be overwritten by future scans' : 'Click to lock this field'}
            >
                {isLocked(field) ? '🔒 Locked' : 'Unlocked'}
            </button>
        </div>
    );

    const inputClass = (field: string) =>
        `w-full bg-gray-950 border border-gray-700 rounded p-2 text-white outline-none focus:border-orange-500 ${isLocked(field) ? 'opacity-60' : ''}`;

    const tabs: { id: MusicEditTab; label: string }[] = hasImageTab
        ? [{ id: 'details', label: 'Details' }, { id: 'image', label: kind === 'artist' ? 'Image' : 'Cover Art' }]
        : [];

    return (
        <Modal isOpen={isOpen} onClose={onClose} size="2xl" surface="gray-900" cardClassName="p-8 flex flex-col max-h-[90vh]">
            <ModalHeader title={title_modal} onClose={onClose} closeDisabled={saving} bordered={false} />
            <div className="border-b border-gray-800 mb-4" />

            <input
                ref={fileInputRef}
                type="file"
                accept="image/jpeg,image/png,image/webp,image/gif"
                onChange={handleArtworkUpload}
                className="hidden"
            />
            <input
                ref={backgroundFileInputRef}
                type="file"
                accept="image/jpeg,image/png,image/webp,image/gif"
                onChange={handleBackgroundUpload}
                className="hidden"
            />
            <input
                ref={bannerFileInputRef}
                type="file"
                accept="image/jpeg,image/png,image/webp,image/gif"
                onChange={handleBannerUpload}
                className="hidden"
            />
            <input
                ref={clearLogoFileInputRef}
                type="file"
                accept="image/png,image/webp"
                onChange={handleClearLogoUpload}
                className="hidden"
            />
            <input
                ref={discArtFileInputRef}
                type="file"
                accept="image/png,image/webp"
                onChange={handleDiscArtUpload}
                className="hidden"
            />

            {hasImageTab && (
                <div className="flex gap-1 mb-6 -mt-2">
                    {tabs.map(tab => (
                        <button
                            key={tab.id}
                            type="button"
                            onClick={() => setActiveTab(tab.id)}
                            className={`px-4 py-2 text-sm font-bold rounded-md transition-colors cursor-pointer ${activeTab === tab.id ? 'bg-gray-800 text-orange-400' : 'text-gray-500 hover:text-gray-200 hover:bg-gray-800/50'}`}
                        >
                            {tab.label}
                        </button>
                    ))}
                </div>
            )}

            <form onSubmit={handleSave} className="flex flex-col flex-1 min-h-0">
                <div className="flex-1 overflow-y-auto pr-1 space-y-4 min-h-0">
                    {kind === 'artist' && activeTab === 'details' && (
                        <>
                            <div>
                                <FieldLabel field="Name" label="Name" />
                                <input type="text" value={name} onChange={e => setName(e.target.value)} className={inputClass('Name')} />
                            </div>
                            <div>
                                <FieldLabel field="SortName" label="Sort Name" />
                                <input type="text" value={sortName} onChange={e => setSortName(e.target.value)} className={inputClass('SortName')} />
                            </div>
                            <div>
                                <FieldLabel field="Biography" label="Biography" />
                                <textarea value={biography} onChange={e => setBiography(e.target.value)} rows={8} className={inputClass('Biography')} />
                            </div>
                        </>
                    )}

                    {kind === 'artist' && activeTab === 'image' && (
                        <>
                            <ArtworkSection
                                label="Artist Image"
                                shape="circle"
                                artworkUrl={artworkUrl}
                                onUrlChange={setArtworkUrl}
                                onUploadClick={() => fileInputRef.current?.click()}
                                uploading={uploading}
                                isLocked={isLocked('ArtworkUrl')}
                                onLockToggle={() => toggleLock('ArtworkUrl')}
                                inputClassName={inputClass('ArtworkUrl')}
                                onLoadSuggestions={() => artist ? musicService.getArtistArtworkSuggestions(artist.id, serverId) : Promise.resolve([])}
                                onRefreshFromProviders={artist ? async () => {
                                    const result = await musicService.refreshArtistArtwork(artist.id, true, serverId);
                                    if (result.updated && result.artworkUrl) {
                                        setArtworkUrl(result.artworkUrl);
                                        onSaved();
                                    }
                                    return result.updated;
                                } : undefined}
                            />
                            <ArtworkSection
                                label="Background Image"
                                shape="wide"
                                artworkUrl={backgroundUrl}
                                onUrlChange={setBackgroundUrl}
                                onUploadClick={() => backgroundFileInputRef.current?.click()}
                                uploading={uploadingBackground}
                                isLocked={isLocked('BackgroundUrl')}
                                onLockToggle={() => toggleLock('BackgroundUrl')}
                                inputClassName={inputClass('BackgroundUrl')}
                                onLoadSuggestions={() => Promise.resolve([])}
                            />
                            <ArtworkSection
                                label="Banner (5:1 strip)"
                                shape="banner"
                                artworkUrl={bannerUrl}
                                onUrlChange={setBannerUrl}
                                onUploadClick={() => bannerFileInputRef.current?.click()}
                                uploading={uploadingBanner}
                                isLocked={isLocked('BannerUrl')}
                                onLockToggle={() => toggleLock('BannerUrl')}
                                inputClassName={inputClass('BannerUrl')}
                                onLoadSuggestions={() => Promise.resolve([])}
                            />
                            <ArtworkSection
                                label="Clear Logo (transparent PNG)"
                                shape="wide"
                                artworkUrl={clearLogoUrl}
                                onUrlChange={setClearLogoUrl}
                                onUploadClick={() => clearLogoFileInputRef.current?.click()}
                                uploading={uploadingClearLogo}
                                isLocked={isLocked('ClearLogoUrl')}
                                onLockToggle={() => toggleLock('ClearLogoUrl')}
                                inputClassName={inputClass('ClearLogoUrl')}
                                onLoadSuggestions={() => Promise.resolve([])}
                            />
                        </>
                    )}

                    {kind === 'album' && activeTab === 'details' && (
                        <>
                            <div>
                                <FieldLabel field="Title" label="Title" />
                                <input type="text" value={title} onChange={e => setTitle(e.target.value)} className={inputClass('Title')} />
                            </div>
                            <div>
                                <FieldLabel field="SortTitle" label="Sort Title" />
                                <input type="text" value={sortTitle} onChange={e => setSortTitle(e.target.value)} className={inputClass('SortTitle')} />
                            </div>
                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <FieldLabel field="Year" label="Year" />
                                    <input type="number" value={year} onChange={e => setYear(e.target.value)} className={inputClass('Year')} />
                                </div>
                                <div>
                                    <FieldLabel field="Genre" label="Genre" />
                                    <input type="text" value={genre} onChange={e => setGenre(e.target.value)} className={inputClass('Genre')} />
                                </div>
                            </div>
                        </>
                    )}

                    {kind === 'album' && activeTab === 'image' && (
                        <>
                            <ArtworkSection
                                label="Cover Art"
                                shape="square"
                                artworkUrl={artworkUrl}
                                onUrlChange={setArtworkUrl}
                                onUploadClick={() => fileInputRef.current?.click()}
                                uploading={uploading}
                                isLocked={isLocked('ArtworkUrl')}
                                onLockToggle={() => toggleLock('ArtworkUrl')}
                                inputClassName={inputClass('ArtworkUrl')}
                                onLoadSuggestions={() => album ? musicService.getAlbumArtworkSuggestions(album.id, serverId) : Promise.resolve([])}
                                onRefreshFromProviders={album ? async () => {
                                    const result = await musicService.refreshAlbumArtwork(album.id, true, serverId);
                                    if (result.updated && result.artworkUrl) {
                                        setArtworkUrl(result.artworkUrl);
                                        onSaved();
                                    }
                                    return result.updated;
                                } : undefined}
                            />
                            <ArtworkSection
                                label="Background Image"
                                shape="wide"
                                artworkUrl={backgroundUrl}
                                onUrlChange={setBackgroundUrl}
                                onUploadClick={() => backgroundFileInputRef.current?.click()}
                                uploading={uploadingBackground}
                                isLocked={isLocked('BackgroundUrl')}
                                onLockToggle={() => toggleLock('BackgroundUrl')}
                                inputClassName={inputClass('BackgroundUrl')}
                                onLoadSuggestions={() => Promise.resolve([])}
                            />
                            <ArtworkSection
                                label="Disc Art (vinyl/CD)"
                                shape="circle"
                                artworkUrl={discArtUrl}
                                onUrlChange={setDiscArtUrl}
                                onUploadClick={() => discArtFileInputRef.current?.click()}
                                uploading={uploadingDiscArt}
                                isLocked={isLocked('DiscArtUrl')}
                                onLockToggle={() => toggleLock('DiscArtUrl')}
                                inputClassName={inputClass('DiscArtUrl')}
                                onLoadSuggestions={() => Promise.resolve([])}
                            />
                        </>
                    )}

                    {kind === 'track' && (
                        <>
                            <div>
                                <FieldLabel field="Title" label="Title" />
                                <input type="text" value={title} onChange={e => setTitle(e.target.value)} className={inputClass('Title')} />
                            </div>
                            <div>
                                <FieldLabel field="SortTitle" label="Sort Title" />
                                <input type="text" value={sortTitle} onChange={e => setSortTitle(e.target.value)} className={inputClass('SortTitle')} />
                            </div>
                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <FieldLabel field="TrackNumber" label="Track #" />
                                    <input type="number" value={trackNumber} onChange={e => setTrackNumber(e.target.value)} className={inputClass('TrackNumber')} />
                                </div>
                                <div>
                                    <FieldLabel field="DiscNumber" label="Disc #" />
                                    <input type="number" value={discNumber} onChange={e => setDiscNumber(e.target.value)} className={inputClass('DiscNumber')} />
                                </div>
                            </div>
                            <div>
                                <FieldLabel field="ContentRating" label="Content Rating" />
                                <input
                                    type="text"
                                    value={contentRating}
                                    onChange={e => setContentRating(e.target.value)}
                                    placeholder="e.g. Explicit, Clean, G, PG, PG-13, R, or leave empty"
                                    className={inputClass('ContentRating')}
                                />
                                <div className="flex flex-wrap gap-2 mt-2">
                                    {RATING_QUICK_PICKS.map(r => (
                                        <button
                                            key={r}
                                            type="button"
                                            onClick={() => setContentRating(r)}
                                            className={`text-xs px-2.5 py-1 rounded font-bold transition-colors cursor-pointer ${contentRating === r ? 'bg-orange-600 text-white' : 'bg-gray-800 text-gray-400 hover:text-white hover:bg-gray-700'}`}
                                        >
                                            {r}
                                        </button>
                                    ))}
                                    <button
                                        type="button"
                                        onClick={() => setContentRating('')}
                                        className="text-xs px-2.5 py-1 rounded font-bold bg-gray-800 text-gray-500 hover:text-red-400 hover:bg-gray-700 transition-colors cursor-pointer"
                                    >
                                        Clear
                                    </button>
                                </div>
                            </div>
                        </>
                    )}
                </div>

                <div className="pt-4 flex gap-2 shrink-0">
                    <button type="button" onClick={onClose} disabled={saving} className="flex-1 bg-gray-800 hover:bg-gray-700 py-2.5 rounded-md font-bold transition-colors cursor-pointer">
                        Cancel
                    </button>
                    <button type="submit" disabled={saving} className="flex-1 bg-orange-600 hover:bg-orange-500 py-2.5 rounded-md font-bold transition-colors cursor-pointer">
                        {saving ? 'Saving...' : 'Save'}
                    </button>
                </div>
            </form>
        </Modal>
    );
}

interface ArtworkSectionProps {
    label: string;
    shape: 'square' | 'circle' | 'wide' | 'banner';
    artworkUrl: string;
    onUrlChange: (url: string) => void;
    onUploadClick: () => void;
    uploading: boolean;
    isLocked: boolean;
    onLockToggle: () => void;
    inputClassName: string;
    onLoadSuggestions: () => Promise<MusicArtworkResultVM[]>;
    onRefreshFromProviders?: () => Promise<boolean>;
}

function ArtworkSection({ label, shape, artworkUrl, onUrlChange, onUploadClick, uploading, isLocked, onLockToggle, inputClassName, onLoadSuggestions, onRefreshFromProviders }: ArtworkSectionProps) {
    const dialog = useDialog();
    const shapeClass = shape === 'circle' ? 'rounded-full' : 'rounded';
    const containerClass = shape === 'banner'
        ? 'w-60 h-12 rounded'
        : shape === 'wide'
        ? 'w-56 h-28 rounded'
        : 'w-28 h-28 ' + shapeClass;
    const hideBrowse = shape === 'wide' || shape === 'banner';

    const [suggestionsOpen, setSuggestionsOpen] = useState(false);
    const [suggestionsLoading, setSuggestionsLoading] = useState(false);
    const [suggestions, setSuggestions] = useState<MusicArtworkResultVM[] | null>(null);
    const [suggestionsError, setSuggestionsError] = useState<string | null>(null);
    const [refreshing, setRefreshing] = useState(false);

    const handleRefresh = async () => {
        if (!onRefreshFromProviders) return;
        setRefreshing(true);
        try {
            const updated = await onRefreshFromProviders();
            if (!updated) {
                await dialog.alert('No artwork found from providers — leaving the current image in place.');
            }
        } catch (err) {
            console.error('Refresh from providers failed', err);
            await dialog.alert('Refresh failed.');
        } finally {
            setRefreshing(false);
        }
    };

    const handleBrowse = async () => {
        const wasOpen = suggestionsOpen;
        setSuggestionsOpen(!wasOpen);
        if (wasOpen) return;
        if (suggestions !== null) return; // already loaded
        setSuggestionsLoading(true);
        setSuggestionsError(null);
        try {
            const results = await onLoadSuggestions();
            setSuggestions(results);
        } catch (err) {
            console.error('Failed to load artwork suggestions', err);
            setSuggestionsError('Failed to load suggestions.');
            setSuggestions([]);
        } finally {
            setSuggestionsLoading(false);
        }
    };

    return (
        <div className="pt-2 border-t border-gray-800/60">
            <div className="flex items-center justify-between mb-3">
                <label className="text-sm font-medium text-gray-400">{label}</label>
                <button
                    type="button"
                    onClick={onLockToggle}
                    className={`text-xs px-2 py-0.5 rounded transition-colors cursor-pointer ${isLocked ? 'bg-orange-600/30 text-orange-400 hover:bg-orange-600/40' : 'bg-gray-800 text-gray-500 hover:text-gray-300'}`}
                    title={isLocked ? "Field is locked — won't be overwritten by future scans" : 'Click to lock this field'}
                >
                    {isLocked ? '🔒 Locked' : 'Unlocked'}
                </button>
            </div>

            <div className="flex gap-4 mb-3">
                <div className={`${containerClass} bg-gray-950 border border-gray-800 flex items-center justify-center overflow-hidden shrink-0`}>
                    {artworkUrl
                        ? <img src={artworkUrl} alt="Preview" className="w-full h-full object-cover" />
                        : <svg className="w-10 h-10 text-gray-700" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg>}
                </div>
                <div className="flex-1 flex flex-col gap-2">
                    <input
                        type="text"
                        value={artworkUrl}
                        onChange={e => onUrlChange(e.target.value)}
                        placeholder="Paste image URL..."
                        className={inputClassName}
                    />
                    <div className="flex gap-2">
                        <button
                            type="button"
                            onClick={onUploadClick}
                            disabled={uploading}
                            className="text-xs px-3 py-1.5 bg-gray-800 hover:bg-gray-700 disabled:opacity-50 text-gray-300 hover:text-white rounded transition-colors cursor-pointer flex items-center gap-1"
                        >
                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16v2a2 2 0 002 2h12a2 2 0 002-2v-2M7 10l5-5m0 0l5 5m-5-5v12" /></svg>
                            {uploading ? 'Uploading...' : 'Upload image'}
                        </button>
                        {!hideBrowse && (
                            <button
                                type="button"
                                onClick={handleBrowse}
                                className="text-xs px-3 py-1.5 bg-gray-800 hover:bg-gray-700 text-gray-300 hover:text-white rounded transition-colors cursor-pointer flex items-center gap-1"
                            >
                                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" /></svg>
                                {suggestionsOpen ? 'Hide alternatives' : 'Browse alternatives'}
                            </button>
                        )}
                        {onRefreshFromProviders && (
                            <button
                                type="button"
                                onClick={handleRefresh}
                                disabled={refreshing}
                                className="text-xs px-3 py-1.5 bg-gray-800 hover:bg-gray-700 disabled:opacity-50 text-gray-300 hover:text-white rounded transition-colors cursor-pointer flex items-center gap-1"
                                title="Pick the best result from configured providers and save it"
                            >
                                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" /></svg>
                                {refreshing ? 'Refreshing...' : 'Refresh from providers'}
                            </button>
                        )}
                    </div>
                </div>
            </div>

            {suggestionsOpen && (
                <div className="bg-gray-950/60 border border-gray-800 rounded p-3">
                    {suggestionsLoading ? (
                        <div className="text-xs text-gray-500 py-2 text-center">Searching providers...</div>
                    ) : suggestionsError ? (
                        <div className="text-xs text-red-400 py-2 text-center">{suggestionsError}</div>
                    ) : suggestions && suggestions.length === 0 ? (
                        <div className="text-xs text-gray-600 py-2 text-center">No alternatives found from metadata providers.</div>
                    ) : suggestions && suggestions.length > 0 ? (
                        <div className="grid grid-cols-3 sm:grid-cols-4 gap-2 max-h-72 overflow-y-auto pr-1">
                            {suggestions.map((s, i) => (
                                <button
                                    key={`${s.url}-${i}`}
                                    type="button"
                                    onClick={() => onUrlChange(s.url)}
                                    className={`relative aspect-square rounded overflow-hidden border-2 transition-all cursor-pointer ${artworkUrl === s.url ? 'border-orange-500 ring-2 ring-orange-500/40' : 'border-gray-800 hover:border-gray-600'}`}
                                    title={`${s.providerName}${s.width && s.height ? ` — ${s.width}×${s.height}` : ''}`}
                                >
                                    <img src={s.thumbnailUrl || s.url} alt="" className="w-full h-full object-cover" />
                                    <div className="absolute bottom-0 inset-x-0 bg-black/70 text-[10px] text-gray-300 py-0.5 px-1 text-center truncate">{s.providerName}</div>
                                </button>
                            ))}
                        </div>
                    ) : null}
                </div>
            )}
        </div>
    );
}
