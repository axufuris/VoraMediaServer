import { useCallback, useEffect, useMemo, useRef, useState, type ComponentType } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { setupGuideService, type SetupGuideVM } from '../../../api/System/setupGuideService';
import { systemSettingsAdminService, type ServerSettings } from '../../../api/System/systemSettingsAdminService';
import { featureFlagsService, type FeatureFlagsVM } from '../../../api/System/featureFlagsService';
import { pluginAdminService, type PluginVM } from '../../../api/System/pluginAdminService';
import { discoveryService, type DiscoveryRowConfig } from '../../../api/Discovery/discoveryService';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';
import { resolveAdminPath } from '../../../components/Admin/Shell/adminNavData';
import { useDialog } from '../../../dialogs';
import { rememberPromptedThisSession } from '../../../components/Admin/SetupGuide/setupGuideSession';
import { SetupSaverContext, type RegisterStepSaver, type StepSaver } from './setupSaver';
import type { SetupStepProps } from './setupContext';
import { SKIPPABLE_STEPS, buildSetupSteps, isSetupStepId, nearestStep, type SetupContent, type SetupStepId } from './setupSteps';
import { StatusMessage } from './SetupParts';
import { ContentStep, PlaybackStep, ServerStep, WelcomeStep } from './steps/BasicsSteps';
import { ArtworkStep, DetectionStep, DiscoverStep, MetadataStep, RatingsStep, RequestsStep, SubtitlesStep, ThumbnailsStep } from './steps/VideoSteps';
import { LiveTvStep, PodcastsStep, RadioStep } from './steps/LiveSteps';
import { AiStep, DoneStep, LastFmStep, LyricsStep } from './steps/MusicAndFinishSteps';

const STEP_COMPONENTS: Record<SetupStepId, ComponentType<SetupStepProps>> = {
    welcome: WelcomeStep, server: ServerStep, playback: PlaybackStep, content: ContentStep,
    metadata: MetadataStep, artwork: ArtworkStep, ratings: RatingsStep, detection: DetectionStep, thumbnails: ThumbnailsStep,
    requests: RequestsStep, subtitles: SubtitlesStep, discover: DiscoverStep,
    livetv: LiveTvStep, radio: RadioStep, podcasts: PodcastsStep,
    lastfm: LastFmStep, lyrics: LyricsStep, ai: AiStep, done: DoneStep,
};

const byOrder = (rows: DiscoveryRowConfig[]) => [...rows].sort((a, b) => a.orderIndex - b.orderIndex);

const contentOf = (guide: SetupGuideVM): SetupContent => ({
    moviesAndShows: guide.moviesAndShows, music: guide.music, liveTv: guide.liveTv, internetRadio: guide.internetRadio, podcasts: guide.podcasts,
});

export default function SetupGuidePage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const navigate = useNavigate();
    const dialog = useDialog();
    const topRef = useRef<HTMLDivElement>(null);

    const [guide, setGuide] = useState<SetupGuideVM | null>(null);
    const [settings, setSettings] = useState<ServerSettings | null>(null);
    const [features, setFeatures] = useState<FeatureFlagsVM | null>(null);
    const [plugins, setPlugins] = useState<PluginVM[]>([]);
    const [discoverRows, setDiscoverRows] = useState<DiscoveryRowConfig[]>([]);
    const [hardwareDevices, setHardwareDevices] = useState<string[]>([]);
    const [stepId, setStepId] = useState<SetupStepId>('welcome');
    const [reached, setReached] = useState(0);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [loadFailed, setLoadFailed] = useState(false);
    const [railOpen, setRailOpen] = useState(false);

    const dirty = useRef({ settings: false, features: false, discover: false });
    const savers = useRef(new Set<StepSaver>());
    const register = useCallback<RegisterStepSaver>(saver => {
        savers.current.add(saver);
        return () => { savers.current.delete(saver); };
    }, []);

    useEffect(() => {
        rememberPromptedThisSession(serverId);
    }, [serverId]);

    useEffect(() => {
        let cancelled = false;
        Promise.all([
            setupGuideService.get(serverId),
            systemSettingsAdminService.getServerSettings(serverId),
            featureFlagsService.getFeatureFlags(serverId),
            pluginAdminService.getPlugins(serverId),
            discoveryService.getAdminConfigs(serverId).catch(() => [] as DiscoveryRowConfig[]),
            systemSettingsAdminService.getHardwareDevices(serverId).catch(() => ['Auto']),
        ]).then(async ([loadedGuide, loadedSettings, loadedFeatures, loadedPlugins, rows, devices]) => {
            if (cancelled) return;
            const steps = buildSetupSteps(contentOf(loadedGuide), rows.length > 0);
            const finished = loadedGuide.status === 'Completed' || loadedGuide.status === 'Skipped';
            const resumeAt = loadedGuide.status === 'InProgress' && isSetupStepId(loadedGuide.step) ? nearestStep(steps, loadedGuide.step) : 'welcome';
            setSettings(loadedSettings);
            setFeatures(loadedFeatures);
            setPlugins(loadedPlugins);
            setDiscoverRows(byOrder(rows));
            setHardwareDevices(devices);
            setStepId(resumeAt);
            setReached(finished ? steps.length - 1 : Math.max(0, steps.findIndex(s => s.id === resumeAt)));
            const started = loadedGuide.status === 'NotStarted'
                ? await setupGuideService.save({ ...loadedGuide, status: 'InProgress', step: 'welcome' }, serverId)
                : loadedGuide;
            if (!cancelled) setGuide(started);
        }).catch(() => { if (!cancelled) setLoadFailed(true); });
        return () => { cancelled = true; };
    }, [serverId]);

    const content = useMemo<SetupContent>(() => guide ? contentOf(guide) : { moviesAndShows: true, music: true, liveTv: false, internetRadio: false, podcasts: false }, [guide]);
    const steps = useMemo(() => buildSetupSteps(content, discoverRows.length > 0), [content, discoverRows.length]);
    const index = Math.max(0, steps.findIndex(s => s.id === stepId));
    const current = steps[index];
    const finished = guide?.status === 'Completed' || guide?.status === 'Skipped';

    const onPluginsChanged = useCallback(() => {
        pluginAdminService.getPlugins(serverId).then(setPlugins).catch(() => { });
        discoveryService.getAdminConfigs(serverId)
            .then(rows => { if (!dirty.current.discover) setDiscoverRows(byOrder(rows)); })
            .catch(() => { });
    }, [serverId]);

    const onSettings = useCallback((patch: Partial<ServerSettings>) => {
        dirty.current.settings = true;
        setSettings(s => s ? { ...s, ...patch } : s);
    }, []);

    const onFeatures = useCallback((patch: Partial<FeatureFlagsVM>) => {
        dirty.current.features = true;
        setFeatures(f => f ? { ...f, ...patch } : f);
    }, []);

    const onContent = useCallback((patch: Partial<SetupContent>) => {
        setGuide(g => g ? { ...g, ...patch } : g);
        const flags: Partial<FeatureFlagsVM> = {};
        if (patch.liveTv !== undefined) flags.liveTvEnabled = patch.liveTv;
        if (patch.internetRadio !== undefined) flags.internetRadioEnabled = patch.internetRadio;
        if (patch.podcasts !== undefined) flags.podcasts = patch.podcasts;
        if (Object.keys(flags).length > 0) onFeatures(flags);
    }, [onFeatures]);

    const onDiscoverRows = useCallback((rows: DiscoveryRowConfig[]) => {
        dirty.current.discover = true;
        setDiscoverRows(rows);
    }, []);

    const saveAll = async () => {
        for (const saver of [...savers.current]) await saver();
        if (dirty.current.settings && settings) {
            await systemSettingsAdminService.updateServerSettings(settings, serverId);
            dirty.current.settings = false;
        }
        if (dirty.current.features && features) {
            await featureFlagsService.updateFeatureFlags(features, serverId);
            dirty.current.features = false;
        }
        if (dirty.current.discover) {
            await discoveryService.updateAdminConfigs(discoverRows.map((r, i) => ({ ...r, orderIndex: i })), serverId);
            dirty.current.discover = false;
        }
    };

    const persist = async (patch: Partial<SetupGuideVM>) => {
        if (!guide) return;
        setGuide(await setupGuideService.save({ ...guide, ...patch }, serverId));
    };

    const goTo = async (target: SetupStepId, save = true) => {
        setBusy(true);
        setError(null);
        try {
            if (save) await saveAll();
            await persist({ step: target, status: finished ? guide?.status : 'InProgress' });
            setStepId(target);
            setRailOpen(false);
            setReached(r => Math.max(r, steps.findIndex(s => s.id === target)));
            topRef.current?.scrollIntoView({ block: 'start' });
        } catch {
            setError("Couldn't save this step. Check the server is reachable and try again.");
        } finally {
            setBusy(false);
        }
    };

    const skipSetup = async () => {
        if (finished) {
            navigate(resolveAdminPath('/admin/settings', serverId));
            return;
        }
        const confirmed = await dialog.confirm({
            title: 'Skip the setup guide?',
            message: "It won't open by itself again. Anything you've already saved stays saved, and you can run the guide any time from System Settings.",
            confirmText: 'Skip setup',
        });
        if (!confirmed) return;
        try {
            await persist({ status: 'Skipped' });
            navigate(resolveAdminPath('/admin', serverId));
        } catch {
            setError("Couldn't skip the guide. Try again.");
        }
    };

    const finish = async () => {
        setBusy(true);
        setError(null);
        try {
            await saveAll();
            await persist({ status: 'Completed', step: 'done' });
            navigate(resolveAdminPath('/admin/libraries/new', serverId));
        } catch {
            setError("Couldn't finish the guide. Try again.");
            setBusy(false);
        }
    };

    if (loadFailed) {
        return (
            <div data-vora-page="">
                <PageHeader title="Set up Vora" />
                <div className="mx-auto max-w-3xl px-4 pt-6 md:px-8"><StatusMessage tone="error">Couldn't load the setup guide. Refresh the page to try again.</StatusMessage></div>
            </div>
        );
    }

    if (!guide || !settings || !features) {
        return (
            <div data-vora-page="">
                <PageHeader title="Set up Vora" />
                <div className="mx-auto max-w-6xl px-4 pt-6 md:px-8"><div className="vora-skeleton h-72" /></div>
            </div>
        );
    }

    const Step = STEP_COMPONENTS[current.id];
    const groups: { name: string; steps: { id: SetupStepId; title: string; index: number }[] }[] = [];
    steps.forEach((s, i) => {
        const last = groups[groups.length - 1];
        if (last && last.name === s.group) last.steps.push({ ...s, index: i });
        else groups.push({ name: s.group, steps: [{ ...s, index: i }] });
    });
    const isLast = current.id === 'done';
    const canJump = (i: number) => finished || i <= reached;
    const progress = Math.round((index / (steps.length - 1)) * 100);

    return (
        <div data-vora-page="" ref={topRef} className="flex min-h-full flex-col">
            <PageHeader
                title="Set up Vora"
                description={current.group ? `${current.group} · ${current.title}` : current.title}
                actions={<button type="button" onClick={skipSetup} className="vora-button-secondary !px-3 !py-1.5 text-xs">{finished ? 'Close guide' : 'Skip setup'}</button>}
            />
            <div className="mx-auto grid w-full max-w-6xl flex-1 gap-7 px-4 pt-6 pb-8 md:px-8 lg:grid-cols-[240px_minmax(0,1fr)]">
                <aside className="flex flex-col gap-3 self-start lg:sticky lg:top-24" aria-label="Setup steps">
                    <div className="flex items-center justify-between gap-2">
                        <span className="text-xs text-[var(--vora-text-muted)]">Step <b className="tabular-nums text-[var(--vora-text-secondary)]">{index + 1}</b> of <b className="tabular-nums text-[var(--vora-text-secondary)]">{steps.length}</b></span>
                        <button type="button" onClick={() => setRailOpen(o => !o)} className="cursor-pointer rounded-[var(--vora-radius-md)] px-2 py-1 text-xs font-semibold text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-surface)] lg:hidden" aria-expanded={railOpen}>
                            {railOpen ? 'Hide steps' : 'All steps'}
                        </button>
                    </div>
                    <div className="h-1 overflow-hidden rounded-full bg-[var(--vora-bg-raised)]" aria-hidden="true">
                        <div className="h-full rounded-full bg-[var(--vora-accent-500)] transition-[width] duration-300" style={{ width: `${progress}%` }} />
                    </div>
                    <nav className={`${railOpen ? 'flex' : 'hidden'} flex-col gap-3 lg:flex`}>
                        {groups.map(group => (
                            <div key={group.name || group.steps[0].id} className="flex flex-col gap-px">
                                {group.name && <div className="px-2.5 pt-2 pb-1 text-[10px] font-bold uppercase tracking-[0.12em] text-[var(--vora-text-muted)]">{group.name}</div>}
                                {group.steps.map(s => {
                                    const isCurrent = s.id === current.id;
                                    const done = !isCurrent && s.index <= reached;
                                    return (
                                        <button
                                            key={s.id}
                                            type="button"
                                            disabled={!canJump(s.index) || busy}
                                            aria-current={isCurrent ? 'step' : undefined}
                                            onClick={() => { if (!isCurrent) void goTo(s.id); }}
                                            className={`flex w-full items-center gap-2.5 rounded-[var(--vora-radius-md)] px-2.5 py-1.5 text-left text-sm transition-colors disabled:cursor-default ${isCurrent
                                                ? 'bg-[var(--vora-bg-surface)] font-semibold text-[var(--vora-text-primary)] shadow-[inset_0_0_0_1px_var(--vora-border-strong)]'
                                                : 'cursor-pointer text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-surface)] disabled:text-[var(--vora-text-muted)] disabled:hover:bg-transparent'}`}
                                        >
                                            <span className={`grid h-[18px] w-[18px] shrink-0 place-items-center rounded-full border-[1.5px] ${isCurrent
                                                ? 'border-[var(--vora-accent-500)] bg-[var(--vora-accent-soft)]'
                                                : done ? 'border-transparent bg-[var(--vora-success-soft)] text-[var(--vora-success-text)]' : 'border-[var(--vora-border-strong)]'}`}>
                                                {isCurrent && <span className="h-1.5 w-1.5 rounded-full bg-[var(--vora-accent-500)]" />}
                                                {done && <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3.5" strokeLinecap="round" aria-hidden="true"><path d="M5 12l5 5L20 7" /></svg>}
                                            </span>
                                            {s.title}
                                        </button>
                                    );
                                })}
                            </div>
                        ))}
                    </nav>
                </aside>
                <div className="flex min-w-0 flex-col gap-5">
                    <SetupSaverContext.Provider value={register}>
                        <Step
                            key={current.id}
                            serverId={serverId}
                            settings={settings}
                            onSettings={onSettings}
                            features={features}
                            onFeatures={onFeatures}
                            content={content}
                            onContent={onContent}
                            plugins={plugins}
                            onPluginsChanged={onPluginsChanged}
                            discoverRows={discoverRows}
                            onDiscoverRows={onDiscoverRows}
                            hardwareDevices={hardwareDevices}
                            goTo={id => { void goTo(id); }}
                        />
                    </SetupSaverContext.Provider>
                </div>
            </div>
            <div className="sticky bottom-0 z-10 border-t border-[var(--vora-border-subtle)] bg-[color-mix(in_srgb,var(--vora-bg-canvas)_90%,transparent)] backdrop-blur-md">
                <div className="mx-auto flex w-full max-w-6xl flex-wrap items-center gap-2.5 px-4 py-3 md:px-8">
                    {index > 0 && <button type="button" onClick={() => void goTo(steps[index - 1].id)} disabled={busy} className="cursor-pointer rounded-[var(--vora-radius-md)] px-3 py-2 text-sm font-semibold text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-surface)] hover:text-[var(--vora-text-primary)] disabled:opacity-50">← Back</button>}
                    <div className="min-w-0 flex-1">{error && <StatusMessage tone="error">{error}</StatusMessage>}</div>
                    {SKIPPABLE_STEPS.includes(current.id) && (
                        <button type="button" onClick={() => void goTo(steps[index + 1].id, false)} disabled={busy} className="cursor-pointer rounded-[var(--vora-radius-md)] px-3 py-2 text-sm font-semibold text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-surface)] hover:text-[var(--vora-text-primary)] disabled:opacity-50">Skip this step</button>
                    )}
                    {isLast
                        ? <button type="button" onClick={finish} disabled={busy} className="vora-button-primary">{busy ? 'Saving…' : 'Add your first library →'}</button>
                        : <button type="button" onClick={() => void goTo(steps[index + 1].id)} disabled={busy} className="vora-button-primary">{busy ? 'Saving…' : index === 0 ? 'Start →' : 'Save and continue →'}</button>}
                </div>
            </div>
        </div>
    );
}
