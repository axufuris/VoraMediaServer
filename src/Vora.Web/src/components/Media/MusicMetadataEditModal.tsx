import { useState, useEffect, useRef } from 'react';
import { useParams } from 'react-router-dom';
import { musicService, type ArtistVM, type AlbumVM, type TrackVM, type MusicArtworkResultVM, type MusicArtworkKind } from '../../api/Music/musicService';
import { Modal, ModalHeader } from '../Common/Modal';
import { pluginAdminService, type PluginOptionVM } from '../../api/System/pluginAdminService';
import { useDialog } from '../../dialogs';

export type MusicEntityKind = 'artist' | 'album' | 'track';
// One image per tab. Four artwork sections stacked in a single tab meant the
// modal scrolled past its own Save button, and the alternatives grid for the
// artist image pushed everything below it off-screen.
type MusicEditTab = 'details' | 'image' | 'background' | 'banner' | 'logo';

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

    // Only the artwork plugins that declare Music. The server does the filtering,
    // so this list cannot drift from what the plugins actually say they support.
    const [artworkProviders, setArtworkProviders] = useState<PluginOptionVM[]>([]);
    const [providerFilter, setProviderFilter] = useState('');

    useEffect(() => {
        if (!isOpen || !hasImageTab) return;
        pluginAdminService.getArtworkProviders(serverId, 'Music')
            .then(setArtworkProviders)
            .catch(() => setArtworkProviders([]));
    }, [isOpen, hasImageTab, serverId]);

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
            <label className="text-sm font-medium text-[var(--vora-text-muted)]">{label}</label>
            <button
                type="button"
                onClick={() => toggleLock(field)}
                className={`text-xs px-2 py-0.5 rounded transition-colors cursor-pointer ${isLocked(field) ? 'bg-[var(--vora-accent-500)]/30 text-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-hover)]/40' : 'bg-[var(--vora-bg-sunken)] text-[var(--vora-text-muted)] hover:text-[var(--vora-text-secondary)]'}`}
                title={isLocked(field) ? 'Field is locked — won\'t be overwritten by future scans' : 'Click to lock this field'}
            >
                {isLocked(field) ? '🔒 Locked' : 'Unlocked'}
            </button>
        </div>
    );

    const inputClass = (field: string) =>
        `w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded p-2 text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)] ${isLocked(field) ? 'opacity-60' : ''}`;

    const tabs: { id: MusicEditTab; label: string }[] = !hasImageTab
        ? []
        : kind === 'artist'
            ? [
                { id: 'details', label: 'Details' },
                { id: 'image', label: 'Image' },
                { id: 'background', label: 'Background' },
                { id: 'banner', label: 'Banner' },
                { id: 'logo', label: 'Clear Logo' },
            ]
            : [
                { id: 'details', label: 'Details' },
                { id: 'image', label: 'Cover Art' },
                { id: 'background', label: 'Background' },
                { id: 'logo', label: 'Disc Art' },
            ];

    // Each slot browses only the images the providers classified as that kind, so
    // a wordmark logo is no longer offered as a candidate artist photo. Unknown
    // stands in for the primary image only, matching the server's own fallback.
    const suggestionsFor = (wanted: MusicArtworkKind[]) => async (): Promise<MusicArtworkResultVM[]> => {
        const all = kind === 'artist'
            ? artist ? await musicService.getArtistArtworkSuggestions(artist.id, serverId) : []
            : album ? await musicService.getAlbumArtworkSuggestions(album.id, serverId) : [];

        // A result names itself by ProviderName ("Fanart.tv"), not by the plugin
        // name ("Fanart.tv Music Artwork"), so the dropdown matches on that.
        const selected = artworkProviders.find(p => p.id === providerFilter);
        return all
            .filter(s => wanted.includes(s.kind))
            .filter(s => !selected || s.providerName === selected.providerName);
    };

    const providerPicker = artworkProviders.length > 1 ? (
        <div className="flex items-center gap-2">
            <label htmlFor="music-artwork-provider" className="text-xs text-[var(--vora-text-muted)]">Provider</label>
            <select
                id="music-artwork-provider"
                value={providerFilter}
                onChange={e => setProviderFilter(e.target.value)}
                className="p-1.5 bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] text-[var(--vora-text-primary)] text-xs rounded outline-none focus:border-[var(--vora-accent-500)] cursor-pointer"
            >
                <option value="">All providers</option>
                {artworkProviders.map(p => (
                    <option key={p.id} value={p.id}>{p.name}</option>
                ))}
            </select>
        </div>
    ) : null;

    return (
        <Modal isOpen={isOpen} onClose={onClose} size="4xl" surface="gray-900" cardClassName="p-8 flex flex-col max-h-[90vh]">
            <ModalHeader title={title_modal} onClose={onClose} closeDisabled={saving} bordered={false} />
            <div className="border-b border-[var(--vora-border-subtle)] mb-4" />

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
                <div className="flex flex-wrap items-center justify-between gap-3 mb-6 -mt-2">
                    <div className="flex gap-1">
                        {tabs.map(tab => (
                            <button
                                key={tab.id}
                                type="button"
                                onClick={() => setActiveTab(tab.id)}
                                className={`px-4 py-2 text-sm font-bold rounded-md transition-colors cursor-pointer ${activeTab === tab.id ? 'bg-[var(--vora-bg-sunken)] text-[var(--vora-accent-500)]' : 'text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] hover:bg-[var(--vora-bg-sunken)]/50'}`}
                            >
                                {tab.label}
                            </button>
                        ))}
                    </div>
                    {/* Beside the tabs rather than inside a slot, because the choice
                        applies to whichever image is open rather than to one of them. */}
                    {activeTab !== 'details' && providerPicker}
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
                            onLoadSuggestions={suggestionsFor(['Thumb', 'Unknown'])}
                            onRefreshFromProviders={artist ? async () => {
                                const result = await musicService.refreshArtistArtwork(artist.id, true, serverId);
                                if (result.updated && result.artworkUrl) {
                                    setArtworkUrl(result.artworkUrl);
                                    onSaved();
                                }
                                return result.updated;
                            } : undefined}
                        />
                    )}

                    {kind === 'artist' && activeTab === 'background' && (
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
                            onLoadSuggestions={suggestionsFor(['Background'])}
                        />
                    )}

                    {kind === 'artist' && activeTab === 'banner' && (
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
                            onLoadSuggestions={suggestionsFor(['Banner'])}
                        />
                    )}

                    {kind === 'artist' && activeTab === 'logo' && (
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
                            onLoadSuggestions={suggestionsFor(['Logo'])}
                        />
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
                            onLoadSuggestions={suggestionsFor(['Cover', 'Unknown'])}
                            onRefreshFromProviders={album ? async () => {
                                const result = await musicService.refreshAlbumArtwork(album.id, true, serverId);
                                if (result.updated && result.artworkUrl) {
                                    setArtworkUrl(result.artworkUrl);
                                    onSaved();
                                }
                                return result.updated;
                            } : undefined}
                        />
                    )}

                    {/* No artwork provider returns an album background today —
                        Fanart.tv's music API has artistbackground with no album
                        equivalent, and TheAudioDB has no album fanart — so Browse
                        comes back empty here. The slot stays because the album
                        header renders it, so upload and paste are the way to set
                        one, and because a plugin that starts returning the
                        Background kind lights it up with no change. */}
                    {kind === 'album' && activeTab === 'background' && (
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
                            onLoadSuggestions={suggestionsFor(['Background'])}
                        />
                    )}

                    {kind === 'album' && activeTab === 'logo' && (
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
                            onLoadSuggestions={suggestionsFor(['Logo'])}
                        />
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
                                            className={`text-xs px-2.5 py-1 rounded font-bold transition-colors cursor-pointer ${contentRating === r ? 'bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)]' : 'bg-[var(--vora-bg-sunken)] text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] hover:bg-[var(--vora-bg-raised)]'}`}
                                        >
                                            {r}
                                        </button>
                                    ))}
                                    <button
                                        type="button"
                                        onClick={() => setContentRating('')}
                                        className="text-xs px-2.5 py-1 rounded font-bold bg-[var(--vora-bg-sunken)] text-[var(--vora-text-muted)] hover:text-[var(--vora-danger-500)] hover:bg-[var(--vora-bg-raised)] transition-colors cursor-pointer"
                                    >
                                        Clear
                                    </button>
                                </div>
                            </div>
                        </>
                    )}
                </div>

                <div className="pt-4 flex gap-2 shrink-0">
                    <button type="button" onClick={onClose} disabled={saving} className="flex-1 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-raised)] py-2.5 rounded-md font-bold transition-colors cursor-pointer">
                        Cancel
                    </button>
                    <button type="submit" disabled={saving} className="flex-1 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-hover)] py-2.5 rounded-md font-bold transition-colors cursor-pointer">
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
        <div className="pt-2 border-t border-[var(--vora-border-subtle)]/60">
            <div className="flex items-center justify-between mb-3">
                <label className="text-sm font-medium text-[var(--vora-text-muted)]">{label}</label>
                <button
                    type="button"
                    onClick={onLockToggle}
                    className={`text-xs px-2 py-0.5 rounded transition-colors cursor-pointer ${isLocked ? 'bg-[var(--vora-accent-500)]/30 text-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-hover)]/40' : 'bg-[var(--vora-bg-sunken)] text-[var(--vora-text-muted)] hover:text-[var(--vora-text-secondary)]'}`}
                    title={isLocked ? "Field is locked — won't be overwritten by future scans" : 'Click to lock this field'}
                >
                    {isLocked ? '🔒 Locked' : 'Unlocked'}
                </button>
            </div>

            <div className="flex gap-4 mb-3">
                <div className={`${containerClass} bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden shrink-0`}>
                    {artworkUrl
                        ? <img src={artworkUrl} alt="Preview" className="w-full h-full object-cover" />
                        : <svg className="w-10 h-10 text-[var(--vora-text-muted)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg>}
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
                            className="text-xs px-3 py-1.5 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-raised)] disabled:opacity-50 text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-1"
                        >
                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16v2a2 2 0 002 2h12a2 2 0 002-2v-2M7 10l5-5m0 0l5 5m-5-5v12" /></svg>
                            {uploading ? 'Uploading...' : 'Upload image'}
                        </button>
                        <button
                            type="button"
                            onClick={handleBrowse}
                            className="text-xs px-3 py-1.5 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-1"
                        >
                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" /></svg>
                            {suggestionsOpen ? 'Hide alternatives' : 'Browse alternatives'}
                        </button>
                        {onRefreshFromProviders && (
                            <button
                                type="button"
                                onClick={handleRefresh}
                                disabled={refreshing}
                                className="text-xs px-3 py-1.5 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-raised)] disabled:opacity-50 text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-1"
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
                <div className="bg-[var(--vora-bg-canvas)]/60 border border-[var(--vora-border-subtle)] rounded p-3">
                    {suggestionsLoading ? (
                        <div className="text-xs text-[var(--vora-text-muted)] py-2 text-center">Searching providers...</div>
                    ) : suggestionsError ? (
                        <div className="text-xs text-[var(--vora-danger-500)] py-2 text-center">{suggestionsError}</div>
                    ) : suggestions && suggestions.length === 0 ? (
                        <div className="text-xs text-[var(--vora-text-muted)] py-2 text-center">No alternatives found from metadata providers.</div>
                    ) : suggestions && suggestions.length > 0 ? (
                        <div className="grid grid-cols-3 sm:grid-cols-4 gap-2 max-h-72 overflow-y-auto pr-1">
                            {suggestions.map((s, i) => (
                                <button
                                    key={`${s.url}-${i}`}
                                    type="button"
                                    onClick={() => onUrlChange(s.url)}
                                    className={`relative aspect-square rounded overflow-hidden border-2 transition-all cursor-pointer ${artworkUrl === s.url ? 'border-orange-500 ring-2 ring-orange-500/40' : 'border-[var(--vora-border-subtle)] hover:border-[var(--vora-border-strong)]'}`}
                                    title={`${s.providerName}${s.width && s.height ? ` — ${s.width}×${s.height}` : ''}`}
                                >
                                    <img src={s.thumbnailUrl || s.url} alt="" className="w-full h-full object-cover" />
                                    <div className="absolute bottom-0 inset-x-0 bg-black/70 text-[10px] text-[var(--vora-text-secondary)] py-0.5 px-1 text-center truncate">{s.providerName}</div>
                                </button>
                            ))}
                        </div>
                    ) : null}
                </div>
            )}
        </div>
    );
}
