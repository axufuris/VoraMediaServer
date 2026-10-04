import type { ReactNode } from 'react';
import { METADATA_LANGUAGES, SCHEDULE_TIME_ZONES, browserTimeZone } from '../../../../utils/serverSettingOptions';
import type { SetupStepProps } from '../setupContext';
import type { SetupContent } from '../setupSteps';
import { ChoiceCard, FieldLabel, Hint, RecommendedBadge, SetupCard, StatusMessage, StepHeading, SwitchRow } from '../SetupParts';

const check = (size: number) => <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" aria-hidden="true"><path d="M5 12l5 5L20 7" /></svg>;

export function WelcomeStep({ settings }: SetupStepProps) {
    const plan: [string, string][] = [
        ['Your server', 'Name, language and time zone'],
        ['Playback', 'How Vora converts video for each device'],
        ["What you'll add", 'Movies, shows, music, Live TV, radio and podcasts'],
        ['Plugins', 'The services that fetch posters, details and ratings'],
        ['Extras', 'Requests, subtitles, lyrics and AI, if you want them'],
    ];
    return (
        <div className="grid items-center gap-6 lg:grid-cols-[minmax(0,1.2fr)_minmax(0,1fr)]">
            <div className="space-y-4">
                <StepHeading
                    eyebrow="Setup guide"
                    title={`Let's get ${settings.serverName || 'your server'} ready`}
                    lead="A few short questions set up how Vora works and connect the services that fill in posters, details and ratings. Every question shows what the server has now, and you can change any of it later."
                />
                <p className="flex items-center gap-2 text-[13px] text-[var(--vora-text-secondary)]">
                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true"><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></svg>
                    About 10 minutes. You can leave and come back; your progress is saved.
                </p>
            </div>
            <ul className="grid gap-2">
                {plan.map(([title, detail]) => (
                    <li key={title} className="grid grid-cols-[24px_minmax(0,1fr)] items-start gap-2.5 rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-surface)] px-3 py-2.5">
                        <span className="mt-0.5 text-[var(--vora-accent-text)]">{check(16)}</span>
                        <span><b className="block font-semibold text-[var(--vora-text-primary)]">{title}</b><span className="text-[13px] text-[var(--vora-text-muted)]">{detail}</span></span>
                    </li>
                ))}
            </ul>
        </div>
    );
}

export function ServerStep({ settings, onSettings }: SetupStepProps) {
    const suggestion = browserTimeZone();
    const zones = suggestion && !SCHEDULE_TIME_ZONES.includes(suggestion) ? [suggestion, ...SCHEDULE_TIME_ZONES] : SCHEDULE_TIME_ZONES;
    return (
        <>
            <StepHeading eyebrow="Basics" title="Your server" lead="These are shown in the apps and decide when scheduled jobs run." />
            <SetupCard>
                <div>
                    <FieldLabel htmlFor="setup-server-name">Server name</FieldLabel>
                    <input id="setup-server-name" className="vora-input" value={settings.serverName} onChange={e => onSettings({ serverName: e.target.value })} placeholder="Vora Server" autoComplete="off" />
                    <Hint>What this server is called in the apps and in emails.</Hint>
                </div>
                <div>
                    <FieldLabel htmlFor="setup-language">Preferred language</FieldLabel>
                    <select id="setup-language" className="vora-input max-w-sm cursor-pointer" value={settings.metadataLanguage || 'eng'} onChange={e => onSettings({ metadataLanguage: e.target.value })}>
                        {METADATA_LANGUAGES.map(([code, label]) => <option key={code} value={code}>{label}</option>)}
                    </select>
                    <Hint>Titles and descriptions are fetched in this language. Anything without a translation stays in its original language.</Hint>
                </div>
                <div>
                    <FieldLabel htmlFor="setup-time-zone">Time zone</FieldLabel>
                    <select id="setup-time-zone" className="vora-input max-w-md cursor-pointer" value={settings.scheduleTimeZone || ''} onChange={e => onSettings({ scheduleTimeZone: e.target.value })}>
                        <option value="">Server default (container clock)</option>
                        {zones.map(tz => <option key={tz} value={tz}>{tz.replace(/_/g, ' ')}</option>)}
                    </select>
                    {suggestion && settings.scheduleTimeZone !== suggestion && (
                        <p className="mt-2 text-[13px] text-[var(--vora-text-secondary)]">
                            Your browser is on <b className="text-[var(--vora-text-primary)]">{suggestion.replace(/_/g, ' ')}</b>.{' '}
                            <button type="button" onClick={() => onSettings({ scheduleTimeZone: suggestion })} className="cursor-pointer font-semibold text-[var(--vora-accent-text)] hover:underline">Use it</button>
                        </p>
                    )}
                    <Hint>Every scheduled time in Vora (nightly scan, intro detection, thumbnails, guide updates) follows this clock. The container clock is usually UTC.</Hint>
                </div>
            </SetupCard>
        </>
    );
}

const PROFILES: { value: number; title: string; description: string; recommended?: boolean }[] = [
    { value: 1, title: 'Direct stream', description: "Play files exactly as they are and only convert when a device can't play them. Best quality, least work for the server.", recommended: true },
    { value: 0, title: 'Follow the app', description: 'Convert whenever an app asks, for example when someone picks a lower quality in the player.' },
    { value: 2, title: 'Save bandwidth', description: 'Compress more to save upload speed. Good for slow home internet and lots of viewers away from home.' },
];

export function PlaybackStep({ settings, onSettings, hardwareDevices }: SetupStepProps) {
    const gpus = hardwareDevices.filter(d => d !== 'Auto');
    return (
        <>
            <StepHeading eyebrow="Basics" title="Playback" lead="Some devices can't play every video format, so Vora converts (transcodes) video on the fly. Choose how readily it does that." />
            <div>
                <FieldLabel>When should Vora convert video?</FieldLabel>
                <div role="radiogroup" aria-label="When should Vora convert video?" className="grid gap-2.5 sm:grid-cols-2 xl:grid-cols-3">
                    {PROFILES.map(p => (
                        <ChoiceCard
                            key={p.value}
                            selected={settings.streamingProfile === p.value}
                            onSelect={() => onSettings({ streamingProfile: p.value })}
                            title={p.title}
                            badge={p.recommended ? <RecommendedBadge /> : undefined}
                            description={p.description}
                        />
                    ))}
                </div>
            </div>
            <SetupCard>
                <SwitchRow
                    id="setup-hw"
                    label="Use the graphics card (hardware acceleration)"
                    description="Converting video on a GPU is many times faster and keeps the CPU free. It needs a graphics card passed through to the Vora container."
                    checked={settings.useHardwareAcceleration}
                    onChange={on => onSettings({ useHardwareAcceleration: on, useHardwareEncoding: on })}
                />
                {settings.useHardwareAcceleration && (
                    <div className="flex flex-col gap-3 border-t border-dashed border-[var(--vora-border-strong)] pt-4">
                        {gpus.length > 0
                            ? <StatusMessage tone="ok">{check(13)} Vora can see a graphics device: <span className="font-mono">{gpus.join(', ')}</span></StatusMessage>
                            : <StatusMessage tone="info">Vora can't see a graphics card in its container. Turn this off unless you've passed one through; Vora falls back to the CPU either way.</StatusMessage>}
                        <div>
                            <FieldLabel htmlFor="setup-hw-device">Device</FieldLabel>
                            <select id="setup-hw-device" className="vora-input max-w-sm cursor-pointer" value={settings.hardwareTranscodingDevice || 'Auto'} onChange={e => onSettings({ hardwareTranscodingDevice: e.target.value })}>
                                {(hardwareDevices.length ? hardwareDevices : ['Auto']).map(d => <option key={d} value={d}>{d === 'Auto' ? 'Auto (recommended)' : d}</option>)}
                            </select>
                            <Hint>Leave on Auto unless the machine has more than one GPU.</Hint>
                        </div>
                    </div>
                )}
            </SetupCard>
        </>
    );
}

const CONTENT: { key: keyof SetupContent; title: string; description: string; icon: ReactNode }[] = [
    { key: 'moviesAndShows', title: 'Movies & TV shows', description: 'Posters, details, ratings, Skip Intro and requests.', icon: <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><rect x="3" y="4" width="18" height="16" rx="2" /><path d="M7 4v16M17 4v16M3 9h4M3 15h4M17 9h4M17 15h4" /></svg> },
    { key: 'music', title: 'Music', description: 'Popularity, artist bios, lyrics and AI playlists.', icon: <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><path d="M9 18V5l11-2v13" /><circle cx="6" cy="18" r="3" /><circle cx="17" cy="16" r="3" /></svg> },
    { key: 'liveTv', title: 'Live TV', description: 'Watch channels from an M3U playlist with a TV guide.', icon: <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><rect x="3" y="6" width="18" height="13" rx="2" /><path d="M8 2l4 4 4-4" /></svg> },
    { key: 'internetRadio', title: 'Internet radio', description: 'Stream radio stations from around the world.', icon: <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><circle cx="12" cy="12" r="2" /><path d="M8.5 8.5a5 5 0 000 7M15.5 8.5a5 5 0 010 7M5.6 5.6a9 9 0 000 12.8M18.4 5.6a9 9 0 010 12.8" /></svg> },
    { key: 'podcasts', title: 'Podcasts', description: 'A catalog of shows your users can subscribe to.', icon: <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><rect x="9" y="3" width="6" height="11" rx="3" /><path d="M5 11a7 7 0 0014 0M12 18v3" /></svg> },
];

export function ContentStep({ content, onContent }: SetupStepProps) {
    return (
        <>
            <StepHeading eyebrow="Basics" title="What will you put on this server?" lead="Pick everything you plan to use. The rest of the guide only asks about what you pick." />
            <div role="group" aria-label="Content" className="grid gap-2.5 sm:grid-cols-2 xl:grid-cols-3">
                {CONTENT.map(c => (
                    <ChoiceCard key={c.key} multiple selected={content[c.key]} onSelect={() => onContent({ [c.key]: !content[c.key] })} title={c.title} description={c.description} icon={c.icon} />
                ))}
            </div>
            <Hint>Live TV, Internet radio and Podcasts you don't pick are hidden from the apps. You can turn them on later under Features.</Hint>
        </>
    );
}
