import { useEffect, useState, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { systemSettingsAdminService, type ServerSettings } from '../../../api/System/systemSettingsAdminService';
import { musicService } from '../../../api/Music/musicService';
import FeatureToggle from '../../../components/Admin/Features/FeatureToggle';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';
import { useDialog } from '../../../dialogs';

function FieldLabel({ children }: { children: React.ReactNode }) {
    return <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">{children}</label>;
}

function FieldHint({ children }: { children: React.ReactNode }) {
    return <p className="text-xs text-[var(--vora-text-muted)] mt-1.5">{children}</p>;
}

export default function ForYouPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const dialog = useDialog();
    const [serverSettings, setServerSettings] = useState<ServerSettings | null>(null);
    const [isSaving, setIsSaving] = useState(false);

    const loadServerSettings = useCallback(async () => {
        try {
            const data = await systemSettingsAdminService.getServerSettings(serverId);
            setServerSettings(data);
        } catch {
            dialog.alert('Failed to load server settings.');
        }
    }, [serverId, dialog]);

    useEffect(() => {
        loadServerSettings();
    }, [loadServerSettings]);

    const handleSave = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        if (!serverSettings) return;
        setIsSaving(true);
        try {
            await systemSettingsAdminService.updateServerSettings(serverSettings, serverId);
            await dialog.alert('Recommendation settings saved.');
        } catch {
            await dialog.alert('Failed to save settings.');
        } finally {
            setIsSaving(false);
        }
    };

    const regenerateNow = async () => {
        try {
            await musicService.refreshRecommendations(serverId);
            await dialog.alert('Mix regeneration started. Mixes will appear shortly.');
        } catch {
            await dialog.alert('Failed to start mix regeneration.');
        }
    };

    return (
        <div data-vora-page="">
            <PageHeader
                title="For You"
                description="Per-profile Daily Mixes and weekly Discover/Mood mixes."
            />

            <div className="px-8 pt-6 pb-10 max-w-6xl mx-auto">
                <FeatureToggle
                    featureKey="forYou"
                    label="Enable For You"
                    description="When off, the For You nav entry is hidden from clients and all recommendation endpoints return 403. The mix-generation engine below stays editable so you can configure ahead of time."
                    serverId={serverId}
                />

                <section>
                        <p className="text-sm text-[var(--vora-text-muted)] mb-4">
                            Controls how Vora generates Daily Mixes, Discover Mix, and Mood Mixes for each profile. These are independent of the For You feature toggle — turning off Daily/Weekly mixes here stops the engine entirely.
                        </p>

                        {!serverSettings ? (
                            <div className="vora-skeleton h-48" />
                        ) : (
                            <form onSubmit={handleSave} className="space-y-6">
                                <section className="vora-card p-6">
                                    <h3 className="text-base font-semibold text-[var(--vora-text-primary)] mb-4 flex items-center gap-2">
                                        <input
                                            type="checkbox"
                                            checked={serverSettings.enableDailyMixes}
                                            onChange={e => setServerSettings({ ...serverSettings, enableDailyMixes: e.target.checked })}
                                            className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                                        />
                                        Daily Mixes
                                    </h3>
                                    <div className={`pl-6 space-y-4 ${!serverSettings.enableDailyMixes ? 'opacity-50' : ''}`}>
                                        <div>
                                            <FieldLabel>Refresh schedule</FieldLabel>
                                            <select
                                                value={serverSettings.dailyMixSchedule}
                                                onChange={e => setServerSettings({ ...serverSettings, dailyMixSchedule: e.target.value })}
                                                disabled={!serverSettings.enableDailyMixes}
                                                className="vora-input max-w-md cursor-pointer"
                                            >
                                                <option value="Daily3am">Every night at 3 AM</option>
                                                <option value="DailyMidnight">Every night at midnight</option>
                                                <option value="Daily6am">Every morning at 6 AM</option>
                                                <option value="Every12Hours">Every 12 hours</option>
                                                <option value="Every6Hours">Every 6 hours</option>
                                                <option value="WeeklySunday3am">Weekly (Sunday 3 AM)</option>
                                                <option value="ManualOnly">Manual only (no schedule)</option>
                                            </select>
                                            <FieldHint>When the background worker regenerates each profile's daily mixes. Use the button below to force a run.</FieldHint>
                                        </div>
                                        <div className="grid grid-cols-2 gap-4 max-w-md">
                                            <div>
                                                <FieldLabel>Mixes per profile</FieldLabel>
                                                <input
                                                    type="number"
                                                    min={1}
                                                    max={12}
                                                    value={serverSettings.dailyMixCount}
                                                    onChange={e => setServerSettings({ ...serverSettings, dailyMixCount: parseInt(e.target.value, 10) || 6 })}
                                                    disabled={!serverSettings.enableDailyMixes}
                                                    className="vora-input"
                                                />
                                            </div>
                                            <div>
                                                <FieldLabel>Tracks per mix</FieldLabel>
                                                <input
                                                    type="number"
                                                    min={10}
                                                    max={200}
                                                    value={serverSettings.dailyMixSize}
                                                    onChange={e => setServerSettings({ ...serverSettings, dailyMixSize: parseInt(e.target.value, 10) || 50 })}
                                                    disabled={!serverSettings.enableDailyMixes}
                                                    className="vora-input"
                                                />
                                            </div>
                                            <div>
                                                <FieldLabel>Drift % per refresh</FieldLabel>
                                                <input
                                                    type="number"
                                                    min={0}
                                                    max={100}
                                                    value={serverSettings.dailyMixDriftPercent}
                                                    onChange={e => setServerSettings({ ...serverSettings, dailyMixDriftPercent: parseInt(e.target.value, 10) || 20 })}
                                                    disabled={!serverSettings.enableDailyMixes}
                                                    className="vora-input"
                                                />
                                                <FieldHint>% of each mix replaced on every refresh.</FieldHint>
                                            </div>
                                            <div>
                                                <FieldLabel>Min plays to enable</FieldLabel>
                                                <input
                                                    type="number"
                                                    min={0}
                                                    value={serverSettings.dailyMixMinPlays}
                                                    onChange={e => setServerSettings({ ...serverSettings, dailyMixMinPlays: parseInt(e.target.value, 10) || 50 })}
                                                    disabled={!serverSettings.enableDailyMixes}
                                                    className="vora-input"
                                                />
                                                <FieldHint>Total weighted plays required before mixes generate.</FieldHint>
                                            </div>
                                        </div>
                                        <div className="flex items-center gap-3 pt-1">
                                            <button
                                                type="button"
                                                onClick={regenerateNow}
                                                disabled={!serverSettings.enableDailyMixes}
                                                className="vora-button-secondary text-xs"
                                            >
                                                Regenerate now
                                            </button>
                                            {serverSettings.dailyMixLastRefreshedAt && (
                                                <span className="text-xs text-[var(--vora-text-muted)]">
                                                    Last refreshed: {new Date(serverSettings.dailyMixLastRefreshedAt).toLocaleString()}
                                                </span>
                                            )}
                                        </div>
                                    </div>
                                </section>

                                <section className="vora-card p-6">
                                    <h3 className="text-base font-semibold text-[var(--vora-text-primary)] mb-2 flex items-center gap-2">
                                        <input
                                            type="checkbox"
                                            checked={serverSettings.enableWeeklyMixes}
                                            onChange={e => setServerSettings({ ...serverSettings, enableWeeklyMixes: e.target.checked })}
                                            className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                                        />
                                        Weekly Mixes
                                    </h3>
                                    <p className="text-sm text-[var(--vora-text-muted)] pl-6">
                                        Generates a Discover Mix and four mood mixes (Focus / Energetic / Chill / Late Night) once a week. Discovery quality improves significantly when Last.fm is connected per-profile.
                                    </p>
                                    {serverSettings.weeklyMixLastRefreshedAt && (
                                        <span className="text-xs text-[var(--vora-text-muted)] pl-6 block mt-2">
                                            Last refreshed: {new Date(serverSettings.weeklyMixLastRefreshedAt).toLocaleString()}
                                        </span>
                                    )}
                                </section>

                                <button type="submit" disabled={isSaving} className="vora-button-primary">
                                    {isSaving ? 'Saving…' : 'Save recommendation settings'}
                                </button>
                            </form>
                        )}
                    </section>
            </div>
        </div>
    );
}
