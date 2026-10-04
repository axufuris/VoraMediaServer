import { useEffect, useState } from 'react';
import { systemSettingsAdminService } from '../../../../api/System/systemSettingsAdminService';
import HealthBadge from '../../../../components/Admin/Primitives/HealthBadge';
import { PLUGIN_FIELDS, findPlugin, hasKeySaved, type SetupStepProps } from '../setupContext';
import SetupPluginCard from '../SetupPluginCard';
import { CardHeading, Hint, SetupCard, StepHeading, SwitchRow, WhyNote } from '../SetupParts';

export function LastFmStep({ plugins, onPluginsChanged, serverId }: SetupStepProps) {
    return (
        <>
            <StepHeading eyebrow="Music" title="Last.fm" lead="Last.fm is a free music service that knows how popular every song and artist is." />
            <WhyNote>
                <b className="font-semibold text-[var(--vora-text-primary)]">What it gives you:</b> popularity on songs and albums ("1.2M listeners"), artist biographies and tags, and better mixes and recommendations.{' '}
                <b className="font-semibold text-[var(--vora-text-primary)]">Scrobbling</b> is optional: each user can link their own Last.fm account so the songs they play are logged on their Last.fm profile.
            </WhyNote>
            <SetupPluginCard pluginId="lastfm_listening" title="Last.fm" subtitle="Popularity, artist bios and scrobbling" recommended="Recommended" fieldKeys={PLUGIN_FIELDS.lastFm} plugin={findPlugin(plugins, 'lastfm_listening')} serverId={serverId} onChanged={onPluginsChanged} />
        </>
    );
}

export function LyricsStep({ plugins, onPluginsChanged, serverId }: SetupStepProps) {
    const lrclib = findPlugin(plugins, 'lrclib_lyrics');
    return (
        <>
            <StepHeading eyebrow="Music" title="Lyrics" lead="Show lyrics in the player, synced line by line when available." />
            {lrclib && (
                <SetupCard>
                    <CardHeading title="LRCLIB" subtitle="Synced lyrics for most popular songs" badges={lrclib.isEnabled ? <HealthBadge tone="ok">On</HealthBadge> : <HealthBadge tone="neutral" showDot={false}>Off</HealthBadge>} />
                    <p className="text-[13px] text-[var(--vora-text-secondary)]">{lrclib.isEnabled ? 'Already working, no key needed.' : 'Turned off in Plugins. Turn it back on there to use it.'}</p>
                </SetupCard>
            )}
            <SetupPluginCard pluginId="genius_lyrics" title="Genius" subtitle="Backup lyrics for songs LRCLIB doesn't have" recommended="Optional backup" fieldKeys={PLUGIN_FIELDS.genius} plugin={findPlugin(plugins, 'genius_lyrics')} serverId={serverId} onChanged={onPluginsChanged} />
        </>
    );
}

export function AiStep({ plugins, onPluginsChanged, serverId, content, settings, onSettings }: SetupStepProps) {
    const uses: [string, string][] = [
        ['Chronological order', 'Puts a collection like the Marvel films in story order without you sorting it by hand.'],
        ['AI lists', 'Builds a collection from a description, such as "90s heist movies", and keeps it up to date.'],
        ['Recommendations', 'Goes beyond the built-in suggestions with themed picks based on what each person watches.'],
        ...(content.music ? [['AI playlists', '"Make me a playlist for a rainy Sunday", plus fresh themed mixes every week.'] as [string, string]] : []),
    ];
    return (
        <>
            <StepHeading eyebrow="AI" title="AI features" lead="With OpenAI, Vora can do jobs you would otherwise do by hand and goes further than its built-in suggestions and mixes." />
            <SetupCard>
                <span className="text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">What it powers</span>
                <ul className="grid gap-2 sm:grid-cols-2">
                    {uses.map(([title, detail]) => (
                        <li key={title} className="rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] px-3 py-2.5">
                            <b className="block font-semibold text-[var(--vora-text-primary)]">{title}</b>
                            <span className="text-[13px] text-[var(--vora-text-muted)]">{detail}</span>
                        </li>
                    ))}
                </ul>
            </SetupCard>
            <SetupPluginCard pluginId="openai_recommendations" title="OpenAI" subtitle="Powers every AI feature. OpenAI charges per use; the monthly limit caps it." fieldKeys={PLUGIN_FIELDS.openAi} plugin={findPlugin(plugins, 'openai_recommendations')} serverId={serverId} onChanged={onPluginsChanged}>
                {content.music && (
                    <div className="border-t border-dashed border-[var(--vora-border-strong)] pt-4">
                        <SwitchRow id="setup-ai-music" label="Turn on AI music playlists" description={'Off by default. Users get "Make me a playlist for…" on the Music page and weekly AI mixes.'} checked={settings.enableAiMusicPlaylists} onChange={on => onSettings({ enableAiMusicPlaylists: on })} />
                    </div>
                )}
            </SetupPluginCard>
        </>
    );
}

type Summary = 'set' | 'skipped' | 'off';

export function DoneStep({ plugins, settings, features, content, discoverRows, serverId }: SetupStepProps) {
    const [aiKey, setAiKey] = useState(false);
    useEffect(() => {
        systemSettingsAdminService.getPluginSettings('openai_recommendations', serverId)
            .then(fields => setAiKey(!!fields.find(f => f.key === 'api_key')?.value?.trim()))
            .catch(() => setAiKey(false));
    }, [serverId]);

    const key = (id: string): Summary => hasKeySaved(plugins, id) ? 'set' : 'skipped';
    const either = (...ids: string[]): Summary => ids.some(id => hasKeySaved(plugins, id)) ? 'set' : 'skipped';
    const rows: [string, Summary][] = [['Your server', 'set'], ['Playback', 'set']];
    if (content.moviesAndShows) {
        rows.push(
            ['Metadata', either('tmdb_metadata', 'tvdb_metadata')],
            ['Artwork', either('fanart_artwork', 'tmdb_metadata', 'tvdb_metadata')],
            ['Ratings', either('omdb_imdb', 'tmdb_metadata')],
            ['Skip intro & credits', settings.runDetections > 0 ? 'set' : 'off'],
            ['Preview thumbnails', settings.videoThumbnailGeneration > 0 ? 'set' : 'off'],
            ['Subtitles', key('opensubtitles_search')],
        );
        if (discoverRows.length > 0) rows.push(['Discover', features.discoverEnabled ? 'set' : 'off']);
    }
    if (content.liveTv) rows.push(['Live TV', features.liveTvEnabled ? 'set' : 'off']);
    if (content.internetRadio) rows.push(['Internet radio', features.internetRadioEnabled ? 'set' : 'off']);
    if (content.podcasts) rows.push(['Podcasts', features.podcasts ? 'set' : 'off']);
    if (content.music) rows.push(['Last.fm', key('lastfm_listening')], ['Lyrics', 'set']);
    rows.push(['AI features', aiKey ? 'set' : 'skipped']);

    const badge = (s: Summary) => s === 'set'
        ? <HealthBadge tone="ok">Set up</HealthBadge>
        : <HealthBadge tone="neutral" showDot={false}>{s === 'off' ? 'Off' : 'Skipped'}</HealthBadge>;

    return (
        <>
            <StepHeading eyebrow="All set" title="The groundwork is set" lead="Your server is configured. The last step is adding your libraries: the folders where your movies, shows and music live. Vora scans them and uses everything you just set up." />
            <ul className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
                {rows.map(([title, state]) => (
                    <li key={title} className="flex min-w-0 items-center justify-between gap-2.5 rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-surface)] px-3 py-2.5">
                        <b className="truncate font-semibold text-[var(--vora-text-primary)]">{title}</b>
                        {badge(state)}
                    </li>
                ))}
            </ul>
            <Hint>Anything skipped can be set up later in Plugins or System Settings, or by running this guide again from System Settings.</Hint>
        </>
    );
}
