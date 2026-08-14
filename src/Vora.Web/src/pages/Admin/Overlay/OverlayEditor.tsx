import { useState, useRef, useEffect, useMemo } from 'react';
import { Rnd } from 'react-rnd';
import { useParams } from 'react-router-dom';
import { overlayService } from '../../../api/Streaming/overlayService';
import { useDialog } from '../../../dialogs';
import FeaturePluginList from '../../../components/Admin/Features/FeaturePluginList';
import FeatureTabs from '../../../components/Admin/Features/FeatureTabs';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';

type MediaType = 'Movie' | 'Season' | 'TvShow' | 'Episode';
type BadgeType = 'resolution' | 'content_rating' | 'audio_codec' | 'stinger' | 'edition' | 'composite_ratings' | 'critic_rating';
type MockType = 'none' | 'watched' | 'unplayed';

interface OverlayElement {
    id: string;
    type: BadgeType;
    previewImage?: string;
    previewImages?: string[];
    xPct: number;
    yPct: number;
    widthPct: number;
    heightPct: number;
}

const PREVIEW_OPTIONS: Record<BadgeType, { label: string, path: string }[]> = {
    resolution: [
        { label: '4K UHD', path: '/Overlays/Resolution/4k.png' },
        { label: '4K Dolby Vision', path: '/Overlays/Resolution/4kdv.png' },
        { label: '4K DV+HDR', path: '/Overlays/Resolution/4kdvhdr.png' },
        { label: '4K DV+HDR10+', path: '/Overlays/Resolution/4kdvhdrplus.png' },
        { label: '4K HDR', path: '/Overlays/Resolution/4khdr.png' },
        { label: '4K HLG', path: '/Overlays/Resolution/4khlg.png' },
        { label: '4K HDR10+', path: '/Overlays/Resolution/4kplus.png' },

        { label: '1080p FHD', path: '/Overlays/Resolution/1080p.png' },
        { label: '1080p Dolby Vision', path: '/Overlays/Resolution/1080pdv.png' },
        { label: '1080p DV+HDR', path: '/Overlays/Resolution/1080pdvhdr.png' },
        { label: '1080p DV+HDR10+', path: '/Overlays/Resolution/1080pdvhdrplus.png' },
        { label: '1080p HDR', path: '/Overlays/Resolution/1080phdr.png' },
        { label: '1080p HLG', path: '/Overlays/Resolution/1080phlg.png' },
        { label: '1080p HDR10+', path: '/Overlays/Resolution/1080pplus.png' },

        { label: '720p HD', path: '/Overlays/Resolution/720p.png' },
        { label: '720p Dolby Vision', path: '/Overlays/Resolution/720pdv.png' },
        { label: '720p DV+HDR', path: '/Overlays/Resolution/720pdvhdr.png' },
        { label: '720p DV+HDR10+', path: '/Overlays/Resolution/720pdvhdrplus.png' },
        { label: '720p HDR', path: '/Overlays/Resolution/720phdr.png' },
        { label: '720p HLG', path: '/Overlays/Resolution/720phlg.png' },
        { label: '720p HDR10+', path: '/Overlays/Resolution/720pplus.png' },

        { label: '576p SD', path: '/Overlays/Resolution/576p.png' },
        { label: '576p Dolby Vision', path: '/Overlays/Resolution/576pdv.png' },
        { label: '576p DV+HDR', path: '/Overlays/Resolution/576pdvhdr.png' },
        { label: '576p DV+HDR10+', path: '/Overlays/Resolution/576pdvhdrplus.png' },
        { label: '576p HDR', path: '/Overlays/Resolution/576phdr.png' },
        { label: '576p HDR10+', path: '/Overlays/Resolution/576pplus.png' },

        { label: '480p SD', path: '/Overlays/Resolution/480p.png' },
        { label: '480p Dolby Vision', path: '/Overlays/Resolution/480pdv.png' },
        { label: '480p DV+HDR', path: '/Overlays/Resolution/480pdvhdr.png' },
        { label: '480p DV+HDR10+', path: '/Overlays/Resolution/480pdvhdrplus.png' },
        { label: '480p HDR', path: '/Overlays/Resolution/480phdr.png' },
        { label: '480p HDR10+', path: '/Overlays/Resolution/480pplus.png' }
    ],
    content_rating: [
        { label: 'G', path: '/Overlays/ContentRating/usg.png' },
        { label: 'G (Color)', path: '/Overlays/ContentRating/usgc.png' },
        { label: 'PG', path: '/Overlays/ContentRating/uspg.png' },
        { label: 'PG (Color)', path: '/Overlays/ContentRating/uspgc.png' },
        { label: 'PG-13', path: '/Overlays/ContentRating/uspg-13.png' },
        { label: 'PG-13 (Color)', path: '/Overlays/ContentRating/uspg-13c.png' },
        { label: 'R', path: '/Overlays/ContentRating/usr.png' },
        { label: 'R (Color)', path: '/Overlays/ContentRating/usrc.png' },
        { label: 'NC-17', path: '/Overlays/ContentRating/usnc-17.png' },
        { label: 'NC-17 (Color)', path: '/Overlays/ContentRating/usnc-17c.png' },
        { label: 'Not Rated', path: '/Overlays/ContentRating/usnr.png' },
        { label: 'Not Rated (Color)', path: '/Overlays/ContentRating/usnrc.png' },

        { label: 'TV-Y', path: '/Overlays/ContentRating/ustv-y.png' },
        { label: 'TV-Y (Color)', path: '/Overlays/ContentRating/ustv-yc.png' },
        { label: 'TV-G', path: '/Overlays/ContentRating/ustv-g.png' },
        { label: 'TV-G (Color)', path: '/Overlays/ContentRating/ustv-gc.png' },
        { label: 'TV-PG', path: '/Overlays/ContentRating/ustv-pg.png' },
        { label: 'TV-PG (Color)', path: '/Overlays/ContentRating/ustv-pgc.png' },
        { label: 'TV-14', path: '/Overlays/ContentRating/ustv-14.png' },
        { label: 'TV-14 (Color)', path: '/Overlays/ContentRating/ustv-14c.png' },
        { label: 'TV-MA', path: '/Overlays/ContentRating/ustv-ma.png' },
        { label: 'TV-MA (Color)', path: '/Overlays/ContentRating/ustv-mac.png' }
    ],
    edition: [
        { label: 'Alternate Cut', path: '/Overlays/Edition/alternate.png' },
        { label: 'Anniversary Edition', path: '/Overlays/Edition/anniversary.png' },
        { label: 'Black & Chrome', path: '/Overlays/Edition/blackchrome.png' },
        { label: 'CODA Cut', path: '/Overlays/Edition/coda.png' },
        { label: 'Collector\'s Edition', path: '/Overlays/Edition/collector.png' },
        { label: 'Criterion Collection', path: '/Overlays/Edition/criterion.png' },
        { label: 'Definitive Edition', path: '/Overlays/Edition/diamond.png' },
        { label: 'Diamond Edition', path: '/Overlays/Edition/diamond.png' },
        { label: 'Director\'s Cut', path: '/Overlays/Edition/directors.png' },
        { label: 'IMAX Enhanced', path: '/Overlays/Edition/enhanced.png' },
        { label: 'Extended Edition', path: '/Overlays/Edition/extended.png' },
        { label: 'Final Cut', path: '/Overlays/Edition/final.png' },
        { label: 'IMAX', path: '/Overlays/Edition/imax.png' },
        { label: 'International Cut', path: '/Overlays/Edition/international.png' },
        { label: 'Open Matte', path: '/Overlays/Edition/openmatte.png' },
        { label: 'Platinum Edition', path: '/Overlays/Edition/platinum.png' },
        { label: 'Producer\'s Cut', path: '/Overlays/Edition/producers.png' },
        { label: 'Remastered', path: '/Overlays/Edition/remastered.png' },
        { label: 'Richard Donner Cut', path: '/Overlays/Edition/richarddonner.png' },
        { label: 'Special Edition', path: '/Overlays/Edition/special.png' },
        { label: 'Theatrical Cut', path: '/Overlays/Edition/theatrical.png' },
        { label: 'Ultimate Cut', path: '/Overlays/Edition/ultimate.png' },
        { label: 'Ulysses Cut', path: '/Overlays/Edition/ulysses.png' },
        { label: 'Uncut Edition', path: '/Overlays/Edition/uncut.png' },
        { label: 'Unrated Edition', path: '/Overlays/Edition/unrated.png' }
    ],
    audio_codec: [
        { label: 'AAC', path: '/Overlays/AudioCodec/aac.png' },
        { label: 'Atmos', path: '/Overlays/AudioCodec/atmos.png' },
        { label: 'Dolby Digital', path: '/Overlays/AudioCodec/digital.png' },
        { label: 'Dolby Atmos', path: '/Overlays/AudioCodec/dolby_atmos.png' },
        { label: 'DTS', path: '/Overlays/AudioCodec/dts.png' },
        { label: 'DTS-ES', path: '/Overlays/AudioCodec/dtses.png' },
        { label: 'DTS-X', path: '/Overlays/AudioCodec/dtsx.png' },
        { label: 'FLAC', path: '/Overlays/AudioCodec/flac.png' },
        { label: 'DTS High-Res', path: '/Overlays/AudioCodec/hra.png' },
        { label: 'DTS Master Audio', path: '/Overlays/AudioCodec/ma.png' },
        { label: 'MP3', path: '/Overlays/AudioCodec/mp3.png' },
        { label: 'Opus', path: '/Overlays/AudioCodec/opus.png' },
        { label: 'PCM', path: '/Overlays/AudioCodec/pcm.png' },
        { label: 'Dolby Digital +', path: '/Overlays/AudioCodec/plus.png' },
        { label: 'Dolby Digital + Atmos', path: '/Overlays/AudioCodec/plus_atmos.png' },
        { label: 'TrueHD', path: '/Overlays/AudioCodec/truehd.png' },
        { label: 'TrueHD Atmos', path: '/Overlays/AudioCodec/truehd_atmos.png' }
    ],
    stinger: [
        { label: 'Stinger', path: '/Overlays/MediaStingers/MediaStinger.png' }
    ],
    critic_rating: [
        { label: 'AniDB', path: '/Overlays/Rating/AniDB.png' },
        { label: 'IMDb', path: '/Overlays/Rating/IMDb.png' },
        { label: 'IMDb Top', path: '/Overlays/Rating/IMDbTop.png' },
        { label: 'IMDb Top 100', path: '/Overlays/Rating/IMDbTop100.png' },
        { label: 'IMDb Top 250', path: '/Overlays/Rating/IMDbTop250.png' },
        { label: 'IMDb Top 1000', path: '/Overlays/Rating/IMDbTop1000.png' },
        { label: 'Letterboxd', path: '/Overlays/Rating/Letterboxd.png' },
        { label: 'MyAnimeList', path: '/Overlays/Rating/MAL.png' },
        { label: 'MDBList', path: '/Overlays/Rating/MDBList.png' },
        { label: 'Metacritic', path: '/Overlays/Rating/Metacritic.png' },
        { label: 'Metacritic Must-See', path: '/Overlays/Rating/MetacriticTop.png' },
        { label: 'Rotten Tomatoes (Audience)', path: '/Overlays/Rating/RT-Aud-Fresh.png' },
        { label: 'Rotten Tomatoes (Aud. Rotten)', path: '/Overlays/Rating/RT-Aud-Rotten.png' },
        { label: 'Rotten Tomatoes (Aud. Top)', path: '/Overlays/Rating/RT-Aud-Top.png' },
        { label: 'Rotten Tomatoes (Critic)', path: '/Overlays/Rating/RT-Crit-Fresh.png' },
        { label: 'Rotten Tomatoes (Crit. Rotten)', path: '/Overlays/Rating/RT-Crit-Rotten.png' },
        { label: 'Rotten Tomatoes (Certified)', path: '/Overlays/Rating/RT-Crit-Top.png' },
        { label: 'Star', path: '/Overlays/Rating/Star.png' },
        { label: 'TMDb', path: '/Overlays/Rating/TMDb.png' },
        { label: 'Trakt', path: '/Overlays/Rating/Trakt.png' }
    ],
    composite_ratings: []
};

const BACKGROUNDS: Record<MediaType, string> = {
    Movie: '/Overlays/Examples/JohnWick.png',
    Season: '/Overlays/Examples/TheMandalorianSeason.png',
    TvShow: '/Overlays/Examples/TheMandalorianSeason.png',
    Episode: '/Overlays/Examples/TheMandalorian.png'
};

export default function OverlayEditor() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const [activeTemplateId, setActiveTemplateId] = useState<string | null>(null);
    const [mediaType, setMediaType] = useState<MediaType>('Movie');
    const [elements, setElements] = useState<OverlayElement[]>([]);
    const [mockUI, setMockUI] = useState<MockType>('none');
    const [activeTab, setActiveTab] = useState<'editor' | 'plugins'>('editor');


    const canvasRef = useRef<HTMLDivElement>(null);
    const [canvasSize, setCanvasSize] = useState({ width: 0, height: 0 });
    const elementIdCounter = useRef(0);
    const nextElementId = () => `el-${++elementIdCounter.current}`;

    useEffect(() => {
        const el = canvasRef.current;
        if (!el) return;

        const updateSize = () => {
            if (canvasRef.current) {
                setCanvasSize({
                    width: canvasRef.current.clientWidth,
                    height: canvasRef.current.clientHeight
                });
            }
        };

        updateSize();
        const observer = new ResizeObserver(updateSize);
        observer.observe(el);
        window.addEventListener('resize', updateSize);

        return () => {
            observer.disconnect();
            window.removeEventListener('resize', updateSize);
        };
    }, [mediaType]);

    useEffect(() => {
        const loadTemplate = async () => {
            try {
                const template = await overlayService.getTemplateByMediaType(mediaType, serverId);

                if (template && template.configurationJson) {
                    setActiveTemplateId(template.id || null);
                    const parsedElements = JSON.parse(template.configurationJson);

                    const restoredElements = parsedElements.map((el: OverlayElement) => {
                        const base = {
                            id: nextElementId(),
                            type: el.type,
                            xPct: el.xPct,
                            yPct: el.yPct,
                            widthPct: el.widthPct,
                            heightPct: el.heightPct
                        };

                        if (el.type === 'composite_ratings') {
                            return {
                                ...base,
                                previewImages: [
                                    '/Overlays/Rating/Star.png',
                                    '/Overlays/Rating/RT-Aud-Fresh.png',
                                    '/Overlays/Rating/RT-Crit-Fresh.png'
                                ]
                            };
                        } else {
                            return {
                                ...base,
                                previewImage: PREVIEW_OPTIONS[el.type as BadgeType][0].path
                            };
                        }
                    });

                    setElements(restoredElements);
                } else {
                    setActiveTemplateId(null);
                    setElements([]);
                }
            } catch (error) {
                console.error("Failed to load template:", error);
                setActiveTemplateId(null);
                setElements([]);
            }
        };

        loadTemplate();
    }, [mediaType, serverId]);

    const hasElement = (type: BadgeType) => elements.some(e => e.type === type);

    const addElement = (type: BadgeType) => {
        if (hasElement(type)) return; // Prevent adding duplicates

        if (type === 'composite_ratings') {
            setElements([...elements, {
                id: nextElementId(),
                type,
                previewImages: [
                    '/Overlays/Rating/Star.png',
                    '/Overlays/Rating/RT-Aud-Fresh.png',
                    '/Overlays/Rating/RT-Crit-Fresh.png'
                ],
                xPct: 0.80, yPct: 0.20,
                widthPct: 0.15, heightPct: 0.35
            }]);
            return;
        }

        const defaultImage = PREVIEW_OPTIONS[type][0].path;
        setElements([...elements, {
            id: nextElementId(),
            type,
            previewImage: defaultImage,
            xPct: 0.05, yPct: 0.05,
            widthPct: 0.25, heightPct: 0.08
        }]);
    };

    const updateElementPreview = (id: string, imagePath: string) => {
        setElements(elements.map(el => el.id === id ? { ...el, previewImage: imagePath } : el));
    };

    const updateCompositePreview = (id: string, slotIndex: number, imagePath: string) => {
        setElements(elements.map(el => {
            if (el.id !== id || !el.previewImages) return el;
            const newImages = [...el.previewImages];
            newImages[slotIndex] = imagePath;
            return { ...el, previewImages: newImages };
        }));
    };

    const removeElement = (id: string) => {
        setElements(elements.filter(el => el.id !== id));
    };

    const handleSave = async () => {
        const runNow = await dialog.confirm({
            title: 'Save Template',
            message: 'Your template has been saved. Would you like to process the entire library now, or wait for the nightly schedule?',
            confirmText: 'Update Library Now',
            cancelText: 'Wait for Schedule'
        });

        const payload = {
            targetMediaType: mediaType,
            configurationJson: JSON.stringify(elements.map(e => ({
                type: e.type,
                xPct: e.xPct,
                yPct: e.yPct,
                widthPct: e.widthPct,
                heightPct: e.heightPct
            })))
        };

        try {
            await overlayService.saveTemplate(payload, serverId);

            if (runNow) {
                await overlayService.triggerGlobalSync(serverId);
                console.log("Immediate sync triggered successfully.");
            }

            await dialog.alert("Template saved successfully.");
        } catch (error) {
            console.error("Error saving overlay template:", error);
            await dialog.alert("An error occurred while saving the template.");
        }
    };

    const handleDelete = async () => {
        if (!activeTemplateId) return;

        const confirmed = await dialog.confirm({
            title: 'Delete Template',
            message: 'Are you sure you want to delete this template? All posters for this media type will be reverted to their original artwork in the background.',
            tone: 'danger',
            confirmText: 'Delete'
        });
        if (!confirmed) return;

        try {
            await overlayService.deleteTemplate(activeTemplateId, serverId);
            await overlayService.triggerGlobalSync(serverId);

            setActiveTemplateId(null);
            setElements([]);
            await dialog.alert("Template deleted. Posters are reverting in the background.");
        } catch (error) {
            console.error("Failed to delete template:", error);
            await dialog.alert("Failed to delete template.");
        }
    };

    const pluginTypes = useMemo(() => ['OverlayEngine'], []);

    return (
        <div data-vora-page="" className="lg:h-full lg:flex lg:flex-col lg:overflow-hidden">
            <PageHeader
                title="Poster Overlays"
                description="Compose badges (resolution, content rating, audio codec, ratings) onto your posters."
            />

            <div className="px-8 pt-2 shrink-0">
                <FeatureTabs
                    tabs={[
                        { key: 'editor', label: 'Template Editor' },
                        { key: 'plugins', label: 'Plugins' },
                    ]}
                    activeKey={activeTab}
                    onChange={k => setActiveTab(k as 'editor' | 'plugins')}
                />
            </div>

            {activeTab === 'editor' && (
            <div className="flex flex-col lg:flex-row gap-6 px-8 pt-4 pb-8 lg:flex-1 lg:min-h-0">

                <div className="w-full lg:w-80 shrink-0 vora-card p-5 lg:overflow-y-auto flex flex-col">
                    <div className="mb-6">
                        <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">Target Canvas</label>
                        <select
                            value={mediaType}
                            onChange={(e) => setMediaType(e.target.value as MediaType)}
                            className="vora-input cursor-pointer"
                        >
                            <option value="Movie">Movie Poster (2:3)</option>
                            <option value="TvShow">TV Show Poster (2:3)</option>
                            <option value="Season">Season Poster (2:3)</option>
                            <option value="Episode">Episode Thumbnail (16:9)</option>
                        </select>
                    </div>

                    <div className="mb-6">
                        <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">Add Badges</label>
                        <div className="grid grid-cols-2 gap-2">
                            {(['resolution', 'content_rating', 'audio_codec', 'edition', 'stinger'] as BadgeType[]).map(type => {
                                const label = type === 'resolution' ? 'Resolution' :
                                              type === 'content_rating' ? 'Content Rating' :
                                              type === 'audio_codec' ? 'Audio' :
                                              type === 'edition' ? 'Edition' : 'Stinger Tag';
                                const disabled = hasElement(type);
                                return (
                                    <button
                                        key={type}
                                        type="button"
                                        onClick={() => addElement(type)}
                                        disabled={disabled}
                                        className={`w-full py-1.5 text-xs font-semibold rounded-[var(--vora-radius-md)] transition-colors ${disabled ? 'bg-[var(--vora-bg-sunken)] text-[var(--vora-text-disabled)] cursor-not-allowed' : 'bg-[var(--vora-bg-sunken)] text-[var(--vora-text-primary)] hover:bg-[var(--vora-border-strong)] cursor-pointer'}`}
                                    >
                                        {label}
                                    </button>
                                );
                            })}
                            <button
                                type="button"
                                onClick={() => addElement('composite_ratings')}
                                disabled={hasElement('composite_ratings')}
                                className={`col-span-2 py-2 text-xs font-bold rounded-[var(--vora-radius-md)] mt-1 transition-colors ${hasElement('composite_ratings') ? 'bg-[var(--vora-bg-sunken)] text-[var(--vora-text-disabled)] cursor-not-allowed' : 'bg-[var(--vora-accent-soft)] border border-[var(--vora-accent-500)]/40 hover:bg-[var(--vora-accent-soft-hover)] text-[var(--vora-accent-text)] cursor-pointer'}`}
                            >
                                + Add Ratings Cluster
                            </button>
                        </div>
                    </div>

                    {elements.length > 0 && (
                        <div className="mb-6 border-t border-[var(--vora-border-subtle)] pt-5">
                            <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-accent-text)] mb-3">Active Layers</label>
                            <div className="space-y-2">
                                {elements.map((el, idx) => (
                                    <div key={el.id} className="bg-[var(--vora-bg-sunken)] p-3 rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] flex flex-col gap-2">
                                        <div className="flex justify-between items-center">
                                            <span className="text-xs font-semibold uppercase text-[var(--vora-text-secondary)]">Layer {idx + 1}: {el.type.replace('_', ' ')}</span>
                                            <button type="button" onClick={() => removeElement(el.id)} className="text-[var(--vora-text-muted)] hover:text-[var(--vora-danger-500)] cursor-pointer">
                                                <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" /></svg>
                                            </button>
                                        </div>

                                        {el.type === 'composite_ratings' ? (
                                            <div className="flex flex-col gap-2 border-t border-[var(--vora-border-subtle)] pt-2">
                                                <span className="text-[10px] text-[var(--vora-text-muted)] uppercase">Top (Admin)</span>
                                                <select value={el.previewImages?.[0] || ''} onChange={(e) => updateCompositePreview(el.id, 0, e.target.value)} className="vora-input !py-1 text-xs">
                                                    {PREVIEW_OPTIONS['critic_rating'].map(opt => <option key={opt.path} value={opt.path}>{opt.label}</option>)}
                                                </select>

                                                <span className="text-[10px] text-[var(--vora-text-muted)] uppercase">Middle (3rd Party 1)</span>
                                                <select value={el.previewImages?.[1] || ''} onChange={(e) => updateCompositePreview(el.id, 1, e.target.value)} className="vora-input !py-1 text-xs">
                                                    {PREVIEW_OPTIONS['critic_rating'].map(opt => <option key={opt.path} value={opt.path}>{opt.label}</option>)}
                                                </select>

                                                <span className="text-[10px] text-[var(--vora-text-muted)] uppercase">Bottom (3rd Party 2)</span>
                                                <select value={el.previewImages?.[2] || ''} onChange={(e) => updateCompositePreview(el.id, 2, e.target.value)} className="vora-input !py-1 text-xs">
                                                    {PREVIEW_OPTIONS['critic_rating'].map(opt => <option key={opt.path} value={opt.path}>{opt.label}</option>)}
                                                </select>
                                            </div>
                                        ) : (
                                            <select value={el.previewImage || ''} onChange={(e) => updateElementPreview(el.id, e.target.value)} className="vora-input !py-1 text-xs">
                                                {PREVIEW_OPTIONS[el.type].map(opt => <option key={opt.path} value={opt.path}>{opt.label}</option>)}
                                            </select>
                                        )}
                                    </div>
                                ))}
                            </div>
                        </div>
                    )}

                    <div className="border-t border-[var(--vora-border-subtle)] pt-5 mt-auto">
                        <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-3">UI Collision Mockup</label>
                        <div className="space-y-2">
                            {(['none', 'watched', 'unplayed'] as MockType[]).map(option => (
                                <label key={option} className="flex items-center gap-3 cursor-pointer group select-none">
                                    <input
                                        type="radio"
                                        checked={mockUI === option}
                                        onChange={() => setMockUI(option)}
                                        className="w-4 h-4 accent-[var(--vora-accent-500)]"
                                    />
                                    <span className="text-sm text-[var(--vora-text-primary)] group-hover:text-[var(--vora-text-secondary)] transition-colors">
                                        {option === 'none' ? 'Clean Poster' : option === 'watched' ? 'Watched Checkmark' : 'Unplayed Count'}
                                    </span>
                                </label>
                            ))}
                        </div>
                    </div>
                </div>

                <div className="flex-1 flex flex-col items-center justify-center relative bg-[#0f1115] rounded-[var(--vora-radius-lg)] border border-[var(--vora-border-subtle)] p-6 lg:min-h-0">

                <div className="flex-1 min-h-0 w-full flex items-center justify-center">
                <div
                    ref={canvasRef}
                    className={`relative bg-[var(--vora-bg-raised)] border-2 border-[var(--vora-border-subtle)] rounded-lg shadow-2xl
                        ${mediaType === 'Episode' ? 'aspect-video w-full max-w-4xl max-h-full' : 'aspect-[2/3] h-full max-h-full max-w-full'}`}
                >
                    <img src={BACKGROUNDS[mediaType]} className="absolute inset-0 w-full h-full object-cover rounded-md" alt="Canvas Mockup" />
                    <div className="absolute inset-0 bg-black/20 pointer-events-none rounded-md"></div>

                    {mockUI === 'watched' && (
                        <div
                            className="absolute bg-black/60 backdrop-blur-sm rounded-full flex items-center justify-center shadow-lg border border-white/10 z-10 pointer-events-none"
                            style={{ top: '4%', right: '4%', width: '14%', aspectRatio: '1' }}
                        >
                            <svg className="w-3/5 h-3/5 text-[var(--vora-text-primary)]" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={3} d="M5 13l4 4L19 7" /></svg>
                        </div>
                    )}
                    {mockUI === 'unplayed' && (
                        <div
                            className="absolute bg-[var(--vora-accent-500)] rounded-full flex items-center justify-center text-[var(--vora-text-primary)] font-bold shadow-lg border border-orange-400/50 z-10 pointer-events-none"
                            style={{ top: '4%', right: '4%', width: '17%', aspectRatio: '1', fontSize: 'clamp(10px, 2.2vw, 20px)' }}
                        >
                            3
                        </div>
                    )}

                    {canvasSize.width > 0 && elements.map((el, index) => {
                        // Match the baked overlay math exactly: badge inner sizing is
                        // proportional to the badge box (percent of measured canvas),
                        // with per-axis padding, so the preview equals the bake on any
                        // aspect and doesn't jump when the browser is zoomed. The
                        // absolute clamps are scaled from the backend's 1280px canvas.
                        const scale = canvasSize.width / 1280;
                        const boxW = el.widthPct * canvasSize.width;
                        const boxH = el.heightPct * canvasSize.height;
                        return (
                            <Rnd
                                key={el.id}
                                bounds="parent"
                                size={{ width: boxW, height: boxH }}
                                position={{ x: el.xPct * canvasSize.width, y: el.yPct * canvasSize.height }}
                                onDragStop={(_e, d) => {
                                    const newEls = [...elements];
                                    newEls[index].xPct = d.x / canvasSize.width;
                                    newEls[index].yPct = d.y / canvasSize.height;
                                    setElements(newEls);
                                }}
                                onResizeStop={(_e, _dir, ref, _delta, position) => {
                                    const newEls = [...elements];
                                    newEls[index].widthPct = parseFloat(ref.style.width) / canvasSize.width;
                                    newEls[index].heightPct = parseFloat(ref.style.height) / canvasSize.height;
                                    newEls[index].xPct = position.x / canvasSize.width;
                                    newEls[index].yPct = position.y / canvasSize.height;
                                    setElements(newEls);
                                }}
                                className="group cursor-move absolute"
                            >
                                {el.type === 'composite_ratings' ? (() => {
                                    const gap = Math.max(2 * scale, boxH * 0.04);
                                    const rowH = (boxH - gap * 2) / 3;
                                    const padX = Math.max(4 * scale, boxW * 0.1);
                                    const innerW = Math.max(1, boxW - padX * 2);
                                    const radius = boxW * 0.1;
                                    const logoMaxH = rowH * 0.40;
                                    const font = Math.max(10 * scale, Math.min(rowH * 0.35, innerW * 0.45));
                                    return (
                                        <div className="w-full h-full flex flex-col pointer-events-none" style={{ gap: `${gap}px` }}>
                                            {el.previewImages?.map((img, i) => {
                                                const imgLower = (img ?? '').toLowerCase();
                                                const isRottenTomatoes = imgLower.includes('rt-') || imgLower.includes('rotten');
                                                const isStar = imgLower.endsWith('star.png');
                                                const dummyLabel = isStar ? "4.0" : isRottenTomatoes ? "82%" : "7.0";
                                                return (
                                                    <div key={i} className="bg-black/65 flex flex-col items-center justify-center w-full" style={{ height: `${rowH}px`, borderRadius: `${radius}px`, paddingLeft: `${padX}px`, paddingRight: `${padX}px` }}>
                                                        <img src={img} className="object-contain" style={{ maxHeight: `${logoMaxH}px`, maxWidth: `${innerW}px` }} alt={`Slot ${i}`} />
                                                        <span className="text-[var(--vora-text-primary)] font-bold leading-none drop-shadow-md" style={{ fontSize: `${font}px` }}>
                                                            {dummyLabel}
                                                        </span>
                                                    </div>
                                                );
                                            })}
                                        </div>
                                    );
                                })() : (() => {
                                    const padX = Math.max(3 * scale, boxW * 0.1);
                                    const padY = Math.max(3 * scale, boxH * 0.1);
                                    const radius = Math.min(padX, padY) * 0.8;
                                    return (
                                        <div className="w-full h-full bg-black/65 flex items-center justify-center pointer-events-none" style={{ borderRadius: `${radius}px`, padding: `${padY}px ${padX}px` }}>
                                            <img src={el.previewImage} alt={el.type} className="max-w-full max-h-full object-contain" />
                                        </div>
                                    );
                                })()}

                                <div className="absolute inset-0 border-2 border-dashed border-orange-500 opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none"></div>
                            </Rnd>
                        );
                    })}
                </div>
                </div>

                    <div className="mt-4 shrink-0 z-10 flex gap-3">
                        <button type="button" onClick={handleSave} className="vora-button-primary px-8">
                            Save Template
                        </button>
                        {activeTemplateId && (
                            <button
                                type="button"
                                onClick={handleDelete}
                                className="px-8 py-2 rounded-[var(--vora-radius-md)] text-sm font-semibold text-[var(--vora-danger-text)] bg-[var(--vora-danger-soft)] hover:bg-[var(--vora-danger-500)] hover:text-[var(--vora-text-primary)] transition-colors cursor-pointer"
                            >
                                Delete & Revert
                            </button>
                        )}
                    </div>
                </div>

            </div>
            )}

            {activeTab === 'plugins' && (
                <div className="px-8 pb-10 max-w-6xl mx-auto pt-6">
                    <p className="text-sm text-[var(--vora-text-muted)] mb-4">Overlay rendering engines that produce the final composited posters.</p>
                    <FeaturePluginList
                        serverId={serverId}
                        pluginTypes={pluginTypes}
                        emptyLabel="No OverlayEngine plugins are installed."
                    />
                </div>
            )}
        </div>
    );
}