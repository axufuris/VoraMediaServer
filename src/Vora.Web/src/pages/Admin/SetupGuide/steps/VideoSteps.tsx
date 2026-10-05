import { useCallback, useEffect, useState } from 'react';
import { requestAdminService, type RequestServerVM } from '../../../../api/Discovery/requestAdminService';
import HealthBadge from '../../../../components/Admin/Primitives/HealthBadge';
import type { ServerSettings } from '../../../../api/System/systemSettingsAdminService';
import { PLUGIN_FIELDS, findPlugin, hasKeySaved, type SetupStepProps } from '../setupContext';
import SetupPluginCard from '../SetupPluginCard';
import SetupRequestServerCard from '../SetupRequestServerCard';
import { CardHeading, CheckRow, FieldLabel, Hint, SetupCard, StepHeading, SwitchRow, WhyNote } from '../SetupParts';

function TmdbSharedCard({ title, subtitle, onText, plugins, goTo }: { title: string; subtitle: string; onText: string } & Pick<SetupStepProps, 'plugins' | 'goTo'>) {
    const on = hasKeySaved(plugins, 'tmdb_metadata');
    return (
        <SetupCard>
            <CardHeading title={title} subtitle={subtitle} badges={on ? <HealthBadge tone="ok">On</HealthBadge> : <HealthBadge tone="neutral" showDot={false}>Needs TMDB</HealthBadge>} />
            {on
                ? <p className="text-[13px] text-[var(--vora-text-secondary)]">{onText}</p>
                : <p className="text-[13px] text-[var(--vora-text-secondary)]">Uses the TMDB key from the Metadata step. <button type="button" onClick={() => goTo('metadata')} className="cursor-pointer font-semibold text-[var(--vora-accent-text)] hover:underline">Add a TMDB key</button></p>}
        </SetupCard>
    );
}

export function MetadataStep({ plugins, onPluginsChanged, serverId }: SetupStepProps) {
    return (
        <>
            <StepHeading eyebrow="Movies & TV" title="Metadata" lead="Metadata is everything about a title that isn't the video: its name, year, plot, cast, genres and episode list." />
            <WhyNote>
                <b className="font-semibold text-[var(--vora-text-primary)]">Why it matters:</b> without it, Vora only knows file names. With it, files are matched to the right film or show and get proper titles, descriptions and episode names.{' '}
                <b className="font-semibold text-[var(--vora-text-primary)]">If you only set up one, use TMDB:</b> it covers both movies and shows.
            </WhyNote>
            <SetupPluginCard pluginId="tmdb_metadata" title="The Movie Database (TMDB)" subtitle="Movies and shows" recommended="Recommended" fieldKeys={PLUGIN_FIELDS.tmdb} plugin={findPlugin(plugins, 'tmdb_metadata')} serverId={serverId} onChanged={onPluginsChanged} />
            <SetupPluginCard pluginId="tvdb_metadata" title="The TV Database (TVDB)" subtitle="Shows, especially anime and alternate episode orders" fieldKeys={PLUGIN_FIELDS.tvdb} plugin={findPlugin(plugins, 'tvdb_metadata')} serverId={serverId} onChanged={onPluginsChanged} />
        </>
    );
}

export function ArtworkStep({ plugins, onPluginsChanged, serverId, goTo }: SetupStepProps) {
    return (
        <>
            <StepHeading eyebrow="Movies & TV" title="Artwork" lead="Posters, backgrounds and logos for every movie and show." />
            <TmdbSharedCard title="TMDB artwork" subtitle="Posters and backgrounds" onText="Already on. It uses your TMDB key, so there's nothing else to do." plugins={plugins} goTo={goTo} />
            <SetupPluginCard pluginId="fanart_artwork" title="Fanart.tv" subtitle="High-quality posters, logos and backgrounds. The same key covers music artwork." recommended="Best posters" fieldKeys={PLUGIN_FIELDS.fanart} plugin={findPlugin(plugins, 'fanart_artwork')} serverId={serverId} onChanged={onPluginsChanged} />
            {findPlugin(plugins, 'mal_artwork') && (
                <SetupPluginCard pluginId="mal_artwork" title="MyAnimeList" subtitle="Official anime posters. It also adds anime rows to Discover." fieldKeys={PLUGIN_FIELDS.mal} plugin={findPlugin(plugins, 'mal_artwork')} serverId={serverId} onChanged={onPluginsChanged} />
            )}
        </>
    );
}

export function RatingsStep({ plugins, onPluginsChanged, serverId, goTo }: SetupStepProps) {
    return (
        <>
            <StepHeading eyebrow="Movies & TV" title="Ratings" lead="Scores shown on posters and detail pages." />
            <TmdbSharedCard title="TMDB ratings" subtitle="TMDB audience score" onText="Already on. It uses your TMDB key." plugins={plugins} goTo={goTo} />
            <SetupPluginCard
                pluginId="omdb_imdb"
                title="OMDb"
                subtitle="IMDb, Rotten Tomatoes and Metacritic scores, from one key"
                recommended="Recommended"
                fieldKeys={PLUGIN_FIELDS.omdb}
                plugin={findPlugin(plugins, 'omdb_imdb')}
                serverId={serverId}
                onChanged={onPluginsChanged}
                note={<WhyNote>The free key allows 1,000 lookups a day, so a large library takes a few days to get every score. Vora stops for the day when they run out and picks up the rest on later library scans.</WhyNote>}
            />
            <WhyNote>New libraries show IMDb and Rotten Tomatoes scores by default, which come from OMDb. Without an OMDb key, Vora uses the TMDB score instead.</WhyNote>
        </>
    );
}

type ScheduleKind = 'detection' | 'thumbnails';

const SCHEDULE_FIELDS: Record<ScheduleKind, { trigger: 'runDetections' | 'videoThumbnailGeneration'; time: 'detectionScheduleTime' | 'videoThumbnailScheduleTime'; gpu: 'analyzeUseHardwareDecode' | 'videoThumbnailUseHardwareDecode'; whenOn: number }> = {
    detection: { trigger: 'runDetections', time: 'detectionScheduleTime', gpu: 'analyzeUseHardwareDecode', whenOn: 1 },
    thumbnails: { trigger: 'videoThumbnailGeneration', time: 'videoThumbnailScheduleTime', gpu: 'videoThumbnailUseHardwareDecode', whenOn: 2 },
};

const TRIGGERS: [number, string, string][] = [
    [1, 'As media is added', 'New movies and episodes are processed shortly after they appear in a library.'],
    [2, 'On a schedule', 'Runs once a day at the time below and catches up on anything new.'],
    [3, 'Both', 'Processes new items right away and does a daily catch-up at the time below.'],
];

function ScheduleCard({ kind, label, description, settings, onSettings }: { kind: ScheduleKind; label: string; description: string } & Pick<SetupStepProps, 'settings' | 'onSettings'>) {
    const f = SCHEDULE_FIELDS[kind];
    const trigger = settings[f.trigger];
    const on = trigger > 0;
    const set = (patch: Partial<ServerSettings>) => onSettings(patch);
    return (
        <SetupCard>
            <SwitchRow id={`setup-${kind}-on`} label={label} description={description} checked={on} onChange={next => set({ [f.trigger]: next ? f.whenOn : 0 })} />
            {on && (
                <div className="flex flex-col gap-4 border-t border-dashed border-[var(--vora-border-strong)] pt-4">
                    <div>
                        <FieldLabel>When should it run?</FieldLabel>
                        <div role="radiogroup" aria-label="When should it run?" className="inline-flex flex-wrap gap-1 rounded-[var(--vora-radius-lg)] border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] p-1">
                            {TRIGGERS.map(([value, title]) => (
                                <button
                                    key={value}
                                    type="button"
                                    role="radio"
                                    aria-checked={trigger === value}
                                    onClick={() => set({ [f.trigger]: value })}
                                    className={`cursor-pointer rounded-[var(--vora-radius-md)] px-3 py-1.5 text-[13px] font-medium ${trigger === value ? 'bg-[var(--vora-bg-raised)] text-[var(--vora-text-primary)] shadow-[inset_0_0_0_1px_var(--vora-border-strong)]' : 'text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)]'}`}
                                >
                                    {title}
                                </button>
                            ))}
                        </div>
                        <Hint>{TRIGGERS.find(t => t[0] === trigger)?.[2]}</Hint>
                    </div>
                    {trigger !== 1 && (
                        <div className="flex flex-wrap items-end gap-3">
                            <div className="w-36">
                                <FieldLabel htmlFor={`setup-${kind}-time`}>Daily at</FieldLabel>
                                <input id={`setup-${kind}-time`} type="time" className="vora-input" value={settings[f.time]} onChange={e => set({ [f.time]: e.target.value })} />
                            </div>
                            <span className="pb-2 text-xs text-[var(--vora-text-muted)]">Time zone: <b className="text-[var(--vora-text-secondary)]">{settings.scheduleTimeZone || 'Server default'}</b></span>
                        </div>
                    )}
                    {settings.useHardwareAcceleration
                        ? <CheckRow id={`setup-${kind}-gpu`} checked={settings[f.gpu]} onChange={gpu => set({ [f.gpu]: gpu })} label="Use the graphics card for this" hint="Faster. Turn off to keep the GPU free for playback or other apps like Tdarr." />
                        : <Hint>Runs on the CPU. Turn on hardware acceleration in Playback to use the graphics card instead.</Hint>}
                </div>
            )}
        </SetupCard>
    );
}

const FIRST_RUN_NOTE = 'The first run on a large library takes a while: every movie and episode already there is processed once, in the background, while Vora keeps working normally. After that only new additions are.';

export function DetectionStep({ settings, onSettings }: SetupStepProps) {
    return (
        <>
            <StepHeading eyebrow="Movies & TV" title="Skip intro & credits" lead="Vora can find the intro and end credits in each episode and movie, so viewers get Skip Intro and Skip Credits buttons." />
            <ScheduleCard kind="detection" label="Detect intros and credits" description="Each file is listened to and scanned once." settings={settings} onSettings={onSettings} />
            <WhyNote>{FIRST_RUN_NOTE}</WhyNote>
            <Hint>Each library can still turn detection off for itself.</Hint>
        </>
    );
}

export function ThumbnailsStep({ settings, onSettings }: SetupStepProps) {
    return (
        <>
            <StepHeading eyebrow="Movies & TV" title="Preview thumbnails" lead="Small pictures that appear when someone drags the progress bar, so they can see where they're jumping to." />
            <ScheduleCard kind="thumbnails" label="Make preview thumbnails" description="Each video is read once to grab frames. They use some disk space." settings={settings} onSettings={onSettings} />
            <WhyNote>{FIRST_RUN_NOTE}</WhyNote>
            <Hint>Libraries choose whether to use them; with this on, new libraries have it ticked.</Hint>
        </>
    );
}

export function RequestsStep({ serverId }: SetupStepProps) {
    const [servers, setServers] = useState<RequestServerVM[] | null>(null);
    const load = useCallback(() => {
        requestAdminService.getServers(serverId).then(setServers).catch(() => setServers([]));
    }, [serverId]);
    useEffect(() => { load(); }, [load]);
    const existing = (provider: string) => servers?.find(s => s.providerId === provider);
    return (
        <>
            <StepHeading eyebrow="Movies & TV" title="Requests" lead="Let your users ask for movies and shows you don't have yet. Vora sends each request to Radarr (movies) or Sonarr (shows), which download it." />
            <WhyNote>
                Radarr and Sonarr can also feed the <b className="font-semibold text-[var(--vora-text-primary)]">Release Calendar</b> with the upcoming releases they track; tick the box in each. Skip this if you don't use them; you can connect them later in System Settings → Request Servers.
            </WhyNote>
            {servers === null
                ? <div className="vora-skeleton h-40" />
                : <div className="grid gap-3.5 xl:grid-cols-2">
                    <SetupRequestServerCard kind="radarr" existing={existing('radarr_requester')} serverId={serverId} onSaved={load} />
                    <SetupRequestServerCard kind="sonarr" existing={existing('sonarr_requester')} serverId={serverId} onSaved={load} />
                </div>}
        </>
    );
}

export function SubtitlesStep({ plugins, onPluginsChanged, serverId, settings, onSettings }: SetupStepProps) {
    return (
        <>
            <StepHeading eyebrow="Movies & TV" title="Subtitles" lead="Vora already uses subtitles inside your files and next to them. OpenSubtitles finds them for videos that have none." />
            <SetupCard>
                <SwitchRow
                    id="setup-subtitles-prepare"
                    label="Get subtitles ready after each scan"
                    description="Copies the text subtitles out of new videos in the background, so they appear the moment someone turns them on. Big files take a while to read; your files aren't changed."
                    checked={settings.preExtractSubtitlesOnScan}
                    onChange={on => onSettings({ preExtractSubtitlesOnScan: on })}
                />
            </SetupCard>
            <SetupPluginCard pluginId="opensubtitles_search" title="OpenSubtitles" subtitle="Download subtitles that aren't in your files" fieldKeys={PLUGIN_FIELDS.openSubtitles} plugin={findPlugin(plugins, 'opensubtitles_search')} serverId={serverId} onChanged={onPluginsChanged} />
        </>
    );
}

export function DiscoverStep({ features, onFeatures, discoverRows, onDiscoverRows }: SetupStepProps) {
    const providers = Array.from(new Set(discoverRows.map(r => r.providerName || r.providerId)));
    const move = (index: number, delta: number) => {
        const rows = [...discoverRows];
        const [row] = rows.splice(index, 1);
        rows.splice(index + delta, 0, row);
        onDiscoverRows(rows.map((r, i) => ({ ...r, orderIndex: i })));
    };
    return (
        <>
            <StepHeading eyebrow="Movies & TV" title="Discover" lead="Discover shows what's popular and coming soon, including titles that aren't in your library, so users can find something new or request it." />
            <SetupCard>
                <SwitchRow id="setup-discover-on" label="Turn on Discover" description={`Adds a Discover page to the apps. Rows come from ${providers.join(' and ')}.`} checked={features.discoverEnabled} onChange={on => onFeatures({ discoverEnabled: on, discover: on })} />
                {features.discoverEnabled && (
                    <div className="flex flex-col gap-3 border-t border-dashed border-[var(--vora-border-strong)] pt-4">
                        <FieldLabel>Rows on the Discover page</FieldLabel>
                        <ul className="overflow-hidden rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)]">
                            {discoverRows.map((row, i) => (
                                <li key={`${row.providerId}-${row.rowId}`} className="flex items-center gap-2 border-t border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] px-3 py-2 first:border-t-0">
                                    <div className="min-w-0 flex-1">
                                        <div className="truncate font-semibold text-[var(--vora-text-primary)]">{row.name}</div>
                                        <div className="truncate text-xs text-[var(--vora-text-muted)]">{row.providerName || row.providerId}</div>
                                    </div>
                                    <button type="button" aria-label={`Move ${row.name} up`} disabled={i === 0} onClick={() => move(i, -1)} className="cursor-pointer rounded-[var(--vora-radius-md)] px-2 py-1 text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-surface)] disabled:cursor-default disabled:opacity-40">↑</button>
                                    <button type="button" aria-label={`Move ${row.name} down`} disabled={i === discoverRows.length - 1} onClick={() => move(i, 1)} className="cursor-pointer rounded-[var(--vora-radius-md)] px-2 py-1 text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-surface)] disabled:cursor-default disabled:opacity-40">↓</button>
                                    <button
                                        type="button"
                                        role="switch"
                                        aria-checked={row.isEnabled}
                                        aria-label={`Show ${row.name}`}
                                        onClick={() => onDiscoverRows(discoverRows.map((r, j) => j === i ? { ...r, isEnabled: !r.isEnabled } : r))}
                                        className={`relative h-6 w-10 shrink-0 cursor-pointer rounded-full transition-colors ${row.isEnabled ? 'bg-[var(--vora-accent-500)]' : 'bg-[var(--vora-border-strong)]'}`}
                                    >
                                        <span className={`absolute top-0.5 left-0.5 h-5 w-5 rounded-full bg-[var(--vora-text-primary)] transition-transform ${row.isEnabled ? 'translate-x-4' : ''}`} />
                                    </button>
                                </li>
                            ))}
                        </ul>
                        <Hint>Rows appear in this order. More rows appear here when another Discover plugin gets a key, such as MyAnimeList for anime.</Hint>
                    </div>
                )}
            </SetupCard>
        </>
    );
}
