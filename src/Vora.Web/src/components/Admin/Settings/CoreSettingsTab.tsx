import { useEffect, useState, useCallback } from 'react';
import { systemSettingsAdminService, type ServerSettings } from '../../../api/System/systemSettingsAdminService';
import { libraryAdminService } from '../../../api/Media/libraryAdminService';
import FolderPathInput from '../FolderBrowser/FolderPathInput';

interface CoreSettingsTabProps {
    serverId?: string;
    scanners: { id: string, name: string }[];
    hardwareDevices: string[];
    showModal: (title: string, message: string, isError?: boolean) => void;
}

function SettingsCard({ title, children, headingControl }: { title: string, children: React.ReactNode, headingControl?: React.ReactNode }) {
    return (
        <section className="vora-card p-6">
            <h3 className="text-base font-semibold text-[var(--vora-text-primary)] mb-4 flex items-center gap-2">
                {headingControl}
                {title}
            </h3>
            {children}
        </section>
    );
}

function FieldLabel({ children }: { children: React.ReactNode }) {
    return <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">{children}</label>;
}

function FieldHint({ children }: { children: React.ReactNode }) {
    return <p className="text-xs text-[var(--vora-text-muted)] mt-2">{children}</p>;
}

function Checkbox({ checked, onChange, label }: { checked: boolean, onChange: (v: boolean) => void, label: string }) {
    return (
        <label className="flex items-center gap-3 cursor-pointer group select-none">
            <input
                type="checkbox"
                checked={checked}
                onChange={e => onChange(e.target.checked)}
                className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
            />
            <span className="text-sm text-[var(--vora-text-primary)] group-hover:text-[var(--vora-text-secondary)] transition-colors">{label}</span>
        </label>
    );
}

export default function CoreSettingsTab({ serverId, scanners, hardwareDevices, showModal }: CoreSettingsTabProps) {
    const [serverSettings, setServerSettings] = useState<ServerSettings | null>(null);
    const [isSaving, setIsSaving] = useState(false);
    const [subTab, setSubTab] = useState<'general' | 'scanning' | 'transcoding' | 'analysis' | 'thumbnails'>('general');

    const loadServerSettings = useCallback(async () => {
        try {
            const data = await systemSettingsAdminService.getServerSettings(serverId);
            setServerSettings(data);
        } catch {
            showModal('Error', 'Failed to load server settings.', true);
        }
    }, [serverId, showModal]);

    useEffect(() => {
        loadServerSettings();
    }, [loadServerSettings]);

    const handleSaveCore = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        if (!serverSettings) return;
        setIsSaving(true);
        try {
            await systemSettingsAdminService.updateServerSettings(serverSettings, serverId);
            showModal('Success', 'Server settings saved successfully.');
        } catch {
            showModal('Error', 'Failed to save server settings.', true);
        } finally {
            setIsSaving(false);
        }
    };

    if (!serverSettings) return <div className="vora-skeleton h-32 mt-6" />;

    return (
        <div className="pt-2 space-y-4">
            <div className="flex flex-wrap gap-1 border-b border-[var(--vora-border-subtle)]">
                {([
                    { key: 'general', label: 'General' },
                    { key: 'scanning', label: 'Scanning' },
                    { key: 'transcoding', label: 'Transcoding' },
                    { key: 'analysis', label: 'Analysis' },
                    { key: 'thumbnails', label: 'Thumbnails' },
                ] as const).map(t => (
                    <button
                        key={t.key}
                        type="button"
                        onClick={() => setSubTab(t.key)}
                        className={`px-3 py-2 text-sm font-medium -mb-px border-b-2 transition-colors ${subTab === t.key ? 'border-[var(--vora-accent-500)] text-[var(--vora-accent-text)]' : 'border-transparent text-[var(--vora-text-muted)] hover:text-[var(--vora-text-secondary)]'}`}
                    >
                        {t.label}
                    </button>
                ))}
            </div>
            <form onSubmit={handleSaveCore} className="space-y-6">
            {subTab === 'general' && (<>
            <SettingsCard title="General">
                <FieldLabel>Server Name</FieldLabel>
                <input
                    type="text"
                    value={serverSettings.serverName || ''}
                    onChange={e => setServerSettings({ ...serverSettings, serverName: e.target.value })}
                    className="vora-input max-w-md"
                    placeholder="Vora Server"
                />
                <FieldHint>The display name of this server across client applications.</FieldHint>
            </SettingsCard>

            <SettingsCard title="Metadata Language">
                <FieldLabel>Preferred Language</FieldLabel>
                <select
                    value={serverSettings.metadataLanguage || 'eng'}
                    onChange={e => setServerSettings({ ...serverSettings, metadataLanguage: e.target.value })}
                    className="vora-input w-56 cursor-pointer"
                >
                    <option value="eng">English</option>
                    <option value="spa">Spanish (Español)</option>
                    <option value="fra">French (Français)</option>
                    <option value="deu">German (Deutsch)</option>
                    <option value="ita">Italian (Italiano)</option>
                    <option value="por">Portuguese (Português)</option>
                    <option value="nld">Dutch (Nederlands)</option>
                    <option value="swe">Swedish (Svenska)</option>
                    <option value="dan">Danish (Dansk)</option>
                    <option value="nor">Norwegian (Norsk)</option>
                    <option value="fin">Finnish (Suomi)</option>
                    <option value="pol">Polish (Polski)</option>
                    <option value="ces">Czech (Čeština)</option>
                    <option value="ell">Greek (Ελληνικά)</option>
                    <option value="hun">Hungarian (Magyar)</option>
                    <option value="tur">Turkish (Türkçe)</option>
                    <option value="rus">Russian (Русский)</option>
                    <option value="ukr">Ukrainian (Українська)</option>
                    <option value="ara">Arabic (العربية)</option>
                    <option value="heb">Hebrew (עברית)</option>
                    <option value="hin">Hindi (हिन्दी)</option>
                    <option value="tha">Thai (ไทย)</option>
                    <option value="jpn">Japanese (日本語)</option>
                    <option value="kor">Korean (한국어)</option>
                    <option value="zho">Chinese (中文)</option>
                </select>
                <FieldHint>Language used when fetching titles and descriptions from metadata providers (TMDB, TVDB). Titles fall back to their original language when no translation exists. Applies to newly scanned or refreshed items.</FieldHint>
            </SettingsCard>
            </>)}

            {subTab === 'transcoding' && (<>
            <SettingsCard title="Global Streaming Profile">
                <FieldLabel>Transcoder Behavior Priority</FieldLabel>
                <select
                    value={serverSettings.streamingProfile || 0}
                    onChange={e => setServerSettings({ ...serverSettings, streamingProfile: parseInt(e.target.value, 10) })}
                    className="vora-input max-w-xl cursor-pointer"
                >
                    <option value={0}>Client Preference (transcode when requested by client)</option>
                    <option value={1}>Direct Stream Preference (avoid transcoding at all costs)</option>
                    <option value={2}>Bandwidth Optimized (aggressively compress to save upload speed)</option>
                </select>
                <FieldHint>Determines how the server mathematically prioritizes video and audio tracks for clients.</FieldHint>
            </SettingsCard>

            <SettingsCard title="Transcoder">
                <div className="space-y-6">
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                        <div>
                            <FieldLabel>Transcode Quality</FieldLabel>
                            <select
                                value={serverSettings.transcodeQuality}
                                onChange={e => setServerSettings({ ...serverSettings, transcodeQuality: Number(e.target.value) })}
                                className="vora-input cursor-pointer"
                            >
                                <option value={0}>Automatic</option>
                                <option value={1}>Prefer higher speed encoding</option>
                                <option value={2}>Prefer higher quality encoding</option>
                                <option value={3}>Make my CPU hurt</option>
                            </select>
                        </div>
                        <div>
                            <FieldLabel>Background x264 Preset</FieldLabel>
                            <select
                                value={serverSettings.backgroundX264Preset}
                                onChange={e => setServerSettings({ ...serverSettings, backgroundX264Preset: Number(e.target.value) })}
                                className="vora-input cursor-pointer"
                            >
                                <option value={0}>Ultra Fast</option>
                                <option value={1}>Super Fast</option>
                                <option value={2}>Very Fast</option>
                                <option value={3}>Faster</option>
                                <option value={4}>Fast</option>
                                <option value={5}>Medium</option>
                                <option value={6}>Slow</option>
                                <option value={7}>Slower</option>
                                <option value={8}>Very Slow</option>
                            </select>
                        </div>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                        <div>
                            <FieldLabel>Transcoder Temp Directory</FieldLabel>
                            <FolderPathInput
                                value={serverSettings.transcoderTempDirectory}
                                onChange={v => setServerSettings({ ...serverSettings, transcoderTempDirectory: v })}
                                placeholder="/transcode"
                                serverId={serverId}
                                modalTitle="Select transcoder temp directory"
                            />
                        </div>
                        <div>
                            <FieldLabel>Throttle Buffer (seconds)</FieldLabel>
                            <input
                                type="number"
                                min={0}
                                value={serverSettings.transcoderThrottleBuffer ?? 60}
                                onChange={e => setServerSettings({ ...serverSettings, transcoderThrottleBuffer: parseInt(e.target.value) || 0 })}
                                className="vora-input"
                            />
                        </div>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-3 gap-6 pt-4 border-t border-[var(--vora-border-subtle)]">
                        <div>
                            <FieldLabel>Hardware Device</FieldLabel>
                            <select
                                value={serverSettings.hardwareTranscodingDevice}
                                onChange={e => setServerSettings({ ...serverSettings, hardwareTranscodingDevice: e.target.value })}
                                className="vora-input cursor-pointer"
                            >
                                {hardwareDevices.map(device => <option key={device} value={device}>{device}</option>)}
                            </select>
                            <FieldHint>Pick which GPU FFmpeg should use. Leave on Auto unless you have multiple GPUs.</FieldHint>
                        </div>
                        <div>
                            <FieldLabel>Max GPU Transcodes</FieldLabel>
                            <select
                                value={serverSettings.maxGpuTranscodes}
                                onChange={e => setServerSettings({ ...serverSettings, maxGpuTranscodes: Number(e.target.value) })}
                                className="vora-input cursor-pointer"
                            >
                                <option value={0}>Unlimited</option>
                                {[...Array(20)].map((_, i) => <option key={i + 1} value={i + 1}>{i + 1}</option>)}
                            </select>
                        </div>
                        <div>
                            <FieldLabel>Max CPU Transcodes</FieldLabel>
                            <select
                                value={serverSettings.maxCpuTranscodes}
                                onChange={e => setServerSettings({ ...serverSettings, maxCpuTranscodes: Number(e.target.value) })}
                                className="vora-input cursor-pointer"
                            >
                                <option value={0}>Unlimited</option>
                                {[...Array(20)].map((_, i) => <option key={i + 1} value={i + 1}>{i + 1}</option>)}
                            </select>
                        </div>
                    </div>

                    <div>
                        <FieldLabel>Max Background Transcodes</FieldLabel>
                        <select
                            value={serverSettings.maxBackgroundTranscodes}
                            onChange={e => setServerSettings({ ...serverSettings, maxBackgroundTranscodes: Number(e.target.value) })}
                            className="vora-input max-w-xs cursor-pointer"
                        >
                            <option value={0}>Unlimited</option>
                            {[...Array(20)].map((_, i) => <option key={i + 1} value={i + 1}>{i + 1}</option>)}
                        </select>
                    </div>

                    <div className="pt-4 border-t border-[var(--vora-border-subtle)]">
                        <FieldLabel>HEVC Video Encoding</FieldLabel>
                        <select
                            value={serverSettings.enableHevcEncoding}
                            onChange={e => setServerSettings({ ...serverSettings, enableHevcEncoding: Number(e.target.value) })}
                            className="vora-input max-w-md cursor-pointer"
                        >
                            <option value={0}>Never</option>
                            <option value={1}>HEVC sources only</option>
                            <option value={2}>Always available</option>
                        </select>
                    </div>

                    <div className="space-y-3 pt-4 border-t border-[var(--vora-border-subtle)]">
                        <Checkbox
                            checked={serverSettings.disableVideoTranscoding}
                            onChange={v => setServerSettings({ ...serverSettings, disableVideoTranscoding: v })}
                            label="Disable video stream transcoding"
                        />
                        <Checkbox
                            checked={serverSettings.enableHdrToneMapping}
                            onChange={v => setServerSettings({ ...serverSettings, enableHdrToneMapping: v })}
                            label="Enable HDR tone mapping"
                        />
                        <div className="flex items-center gap-3 pt-1 pl-7">
                            <label className="text-xs font-medium text-[var(--vora-text-secondary)] w-44">Tonemapping algorithm</label>
                            <select
                                value={serverSettings.tonemappingAlgorithm || 'hable'}
                                onChange={e => setServerSettings({ ...serverSettings, tonemappingAlgorithm: e.target.value })}
                                disabled={!serverSettings.enableHdrToneMapping}
                                className="vora-input w-56 cursor-pointer disabled:opacity-50"
                            >
                                <option value="linear">Linear</option>
                                <option value="gamma">Gamma</option>
                                <option value="clip">Clip</option>
                                <option value="reinhard">Reinhard</option>
                                <option value="hable">Hable</option>
                                <option value="mobius">Mobius</option>
                            </select>
                        </div>
                        <Checkbox
                            checked={serverSettings.useHardwareAcceleration}
                            onChange={v => setServerSettings({ ...serverSettings, useHardwareAcceleration: v })}
                            label="Use hardware acceleration when available"
                        />
                        <Checkbox
                            checked={serverSettings.useHardwareEncoding}
                            onChange={v => setServerSettings({ ...serverSettings, useHardwareEncoding: v })}
                            label="Use hardware-accelerated video encoding"
                        />
                        <Checkbox
                            checked={serverSettings.enableHevcOptimization}
                            onChange={v => setServerSettings({ ...serverSettings, enableHevcOptimization: v })}
                            label="Enable HEVC optimization"
                        />
                    </div>
                </div>
            </SettingsCard>
            </>)}

            {subTab === 'general' && (<>
            <SettingsCard title="In-Memory Cache">
                <FieldLabel>Cache Size Limit (MB)</FieldLabel>
                <input
                    type="number"
                    min={64}
                    step={64}
                    value={serverSettings.cacheSizeLimitMb ?? 10240}
                    onChange={e => setServerSettings({ ...serverSettings, cacheSizeLimitMb: parseInt(e.target.value, 10) || 10240 })}
                    className="vora-input max-w-xs"
                    placeholder="10240"
                />
                <FieldHint>Maximum size of Vora's in-memory cache (recommendations, device flags, etc.). Default 10240 MB. Changes take effect after restart.</FieldHint>
            </SettingsCard>
            </>)}

            {subTab === 'scanning' && (<>
            <SettingsCard
                title="Nightly Library Scan"
                headingControl={
                    <input
                        type="checkbox"
                        checked={serverSettings.enableNightlyScan}
                        onChange={e => setServerSettings({ ...serverSettings, enableNightlyScan: e.target.checked })}
                        className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                    />
                }
            >
                <div className="pl-7">
                    <FieldLabel>Scheduled Time</FieldLabel>
                    <input
                        type="time"
                        value={serverSettings.nightlyScanTime}
                        onChange={e => setServerSettings({ ...serverSettings, nightlyScanTime: e.target.value })}
                        disabled={!serverSettings.enableNightlyScan}
                        className="vora-input w-auto disabled:opacity-50"
                    />
                    <FieldHint>Vora will scan folders and refresh metadata at this time each night.</FieldHint>
                </div>
            </SettingsCard>

            <SettingsCard title="Ignored Folders">
                <FieldLabel>Folder Names to Skip</FieldLabel>
                <textarea
                    value={(serverSettings.scanIgnoredFolders || []).join('\n')}
                    onChange={e => setServerSettings({ ...serverSettings, scanIgnoredFolders: e.target.value.split('\n') })}
                    rows={4}
                    className="vora-input w-full"
                    placeholder=".recycle"
                />
                <FieldHint>One folder name per line. Any folder in your media paths whose name matches (and everything inside it) is skipped during scanning — useful for recycle bins like .recycle or in-progress transcode folders. Matching is case-insensitive and applies to every library.</FieldHint>
            </SettingsCard>

            <SettingsCard
                title="Missing Media (Trash)"
                headingControl={
                    <input
                        type="checkbox"
                        checked={serverSettings.enableTrashAutoPurge}
                        onChange={e => setServerSettings({ ...serverSettings, enableTrashAutoPurge: e.target.checked })}
                        className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                    />
                }
            >
                <div className="pl-7">
                    <FieldLabel>Auto-delete after (days)</FieldLabel>
                    <input
                        type="number"
                        min={1}
                        value={serverSettings.missingMediaRetentionDays}
                        onChange={e => setServerSettings({ ...serverSettings, missingMediaRetentionDays: parseInt(e.target.value) || 0 })}
                        disabled={!serverSettings.enableTrashAutoPurge}
                        className="vora-input w-auto disabled:opacity-50"
                    />
                    <FieldHint>
                        When a media file disappears (e.g. moved out for transcoding), the item is hidden and moved to Trash instead of being deleted, so watch progress and ratings are kept. If the file returns, the item is restored automatically. Items left in Trash longer than this are permanently deleted. Uncheck to keep missing items forever. Review them under Library → Media Trash.
                    </FieldHint>
                </div>
            </SettingsCard>

            <SettingsCard
                title="Resolve TVDB ids for movies & shows"
                headingControl={
                    <input
                        type="checkbox"
                        checked={serverSettings.resolveMovieTvdbIds}
                        onChange={e => setServerSettings({ ...serverSettings, resolveMovieTvdbIds: e.target.checked })}
                        className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                    />
                }
            >
                <div className="pl-7">
                    <FieldHint>
                        Media from TMDB has no stored TVDB id, so the TVDB artwork provider can't contribute posters/backdrops. TV shows carry a TVDB id in TMDB's data, so they're filled in automatically during metadata refresh at no extra cost. Movies aren't cross-referenced by TMDB — when this is enabled, each movie missing an id is looked up on TVDB (by IMDb id, then title/year) and stored. Requires a configured TVDB provider; coverage is sparse for movies, so many won't resolve.
                    </FieldHint>
                    {serverSettings.resolveMovieTvdbIds && (
                        <div className="mt-4">
                            <button
                                type="button"
                                onClick={async () => {
                                    try {
                                        await libraryAdminService.resolveTvdbIds(serverId);
                                        showModal('Task queued', 'Resolving TVDB ids for movies and shows that are missing one — watch progress under Background Tasks.');
                                    } catch {
                                        showModal('Failed', 'Could not queue the TVDB id resolution.', true);
                                    }
                                }}
                                className="vora-button-secondary"
                            >
                                Resolve now for existing movies &amp; shows
                            </button>
                            <FieldHint>One-time backfill for items already in your library. New items get their id automatically on their first scan.</FieldHint>
                        </div>
                    )}
                </div>
            </SettingsCard>

            <SettingsCard title="Merge duplicate TV shows">
                <FieldHint>
                    If the same show was scanned into two entries (for example a 1080p and a 4K copy in separate folders), this consolidates them into one show. Each episode keeps every file version as a selectable quality, and watch progress and ratings from both copies are merged. Nothing on disk is deleted. Runs across all libraries in the background.
                </FieldHint>
                <div className="mt-4">
                    <button
                        type="button"
                        onClick={async () => {
                            try {
                                await libraryAdminService.mergeDuplicateShows(serverId);
                                showModal('Task queued', 'Merging duplicate TV shows — watch progress under Background Tasks.');
                            } catch {
                                showModal('Failed', 'Could not queue the duplicate-show merge.', true);
                            }
                        }}
                        className="vora-button-secondary"
                    >
                        Merge duplicate shows now
                    </button>
                </div>
            </SettingsCard>

            {scanners.length > 1 && (
                <SettingsCard title="Local Media Scanner Engine">
                    <FieldLabel>Active Scanner Plugin</FieldLabel>
                    <select
                        value={serverSettings.localMediaScannerProviderId}
                        onChange={e => setServerSettings({ ...serverSettings, localMediaScannerProviderId: e.target.value })}
                        className="vora-input max-w-md cursor-pointer"
                    >
                        {scanners.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}
                    </select>
                    <FieldHint>Pick which plugin parses your raw files and folder structures.</FieldHint>
                </SettingsCard>
            )}
            </>)}

            {subTab === 'general' && (<>
            <SettingsCard title="New User Registration">
                <FieldLabel>Registration Mode</FieldLabel>
                <select
                    value={serverSettings.registrationMode || 1}
                    onChange={e => setServerSettings({ ...serverSettings, registrationMode: Number(e.target.value) })}
                    className="vora-input max-w-md cursor-pointer"
                >
                    <option value={0}>Disabled (no new users allowed)</option>
                    <option value={1}>Simple (open registration)</option>
                    <option value={2}>Invite PIN (requires 4-digit code from admin)</option>
                    <option value={3}>Invitation only (admin must send an email invite from /admin/invitations)</option>
                </select>
                <FieldHint>Control how new users can create accounts on this server.</FieldHint>
            </SettingsCard>
            </>)}

            {subTab === 'analysis' && (<>
            <SettingsCard title="Intro & Credit Detection">
                <div className="space-y-4">
                    <div>
                        <FieldLabel>Trigger Condition</FieldLabel>
                        <select
                            value={serverSettings.runDetections}
                            onChange={e => setServerSettings({ ...serverSettings, runDetections: Number(e.target.value) })}
                            className="vora-input max-w-md cursor-pointer"
                        >
                            <option value={0}>Never run</option>
                            <option value={1}>On media addition only</option>
                            <option value={2}>On schedule only</option>
                            <option value={3}>On addition & schedule</option>
                        </select>
                        <FieldHint>Controls when Vora automatically detects intro and credit markers (analyzing audio and video). "Never run" means detection only happens when you click Analyze on a library or item. Scans and metadata refreshes will not trigger it.</FieldHint>
                    </div>
                    {(serverSettings.runDetections === 2 || serverSettings.runDetections === 3) && (
                        <div>
                            <FieldLabel>Scheduled Time</FieldLabel>
                            <input
                                type="time"
                                value={serverSettings.detectionScheduleTime}
                                onChange={e => setServerSettings({ ...serverSettings, detectionScheduleTime: e.target.value })}
                                className="vora-input w-auto"
                            />
                        </div>
                    )}
                </div>
            </SettingsCard>

            <SettingsCard title="Detection Tuning">
                <div className="space-y-4">
                    <div>
                        <FieldLabel>Silence threshold offset (dB)</FieldLabel>
                        <input
                            type="number"
                            min={-40}
                            max={0}
                            value={serverSettings.silenceThresholdOffsetDb}
                            onChange={e => setServerSettings({ ...serverSettings, silenceThresholdOffsetDb: parseInt(e.target.value) || -12 })}
                            className="vora-input w-32"
                        />
                        <FieldHint>How many dB below the file's mean volume counts as silence. -12 dB is a balanced default. Lower values (-18, -24) catch quieter mixes; higher values (-6) only flag near-perfect silence.</FieldHint>
                    </div>
                    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                        <div>
                            <FieldLabel>Min silence duration — movies (seconds)</FieldLabel>
                            <input
                                type="number"
                                min={0.2}
                                max={10}
                                step={0.1}
                                value={serverSettings.silenceMinDurationMovieSec}
                                onChange={e => setServerSettings({ ...serverSettings, silenceMinDurationMovieSec: parseFloat(e.target.value) || 1.5 })}
                                className="vora-input w-32"
                            />
                        </div>
                        <div>
                            <FieldLabel>Min silence duration — episodes (seconds)</FieldLabel>
                            <input
                                type="number"
                                min={0.2}
                                max={10}
                                step={0.1}
                                value={serverSettings.silenceMinDurationEpisodeSec}
                                onChange={e => setServerSettings({ ...serverSettings, silenceMinDurationEpisodeSec: parseFloat(e.target.value) || 1.0 })}
                                className="vora-input w-32"
                            />
                        </div>
                    </div>
                    <div>
                        <FieldLabel>Min black-frame duration (seconds)</FieldLabel>
                        <input
                            type="number"
                            min={0.1}
                            max={10}
                            step={0.1}
                            value={serverSettings.blackFrameMinDurationSec}
                            onChange={e => setServerSettings({ ...serverSettings, blackFrameMinDurationSec: parseFloat(e.target.value) || 0.5 })}
                            className="vora-input w-32"
                        />
                        <FieldHint>Minimum length of solid black frames to count as a scene boundary. Combined with silence to locate intro/credit boundaries.</FieldHint>
                    </div>
                    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                        <div>
                            <FieldLabel>TV season cluster tolerance (seconds)</FieldLabel>
                            <input
                                type="number"
                                min={1}
                                max={60}
                                value={serverSettings.episodeIntroClusterToleranceSec}
                                onChange={e => setServerSettings({ ...serverSettings, episodeIntroClusterToleranceSec: parseInt(e.target.value) || 5 })}
                                className="vora-input w-32"
                            />
                            <FieldHint>±N seconds counts as the same intro/credits position across a season.</FieldHint>
                        </div>
                        <div>
                            <FieldLabel>TV season cluster min agreement (%)</FieldLabel>
                            <input
                                type="number"
                                min={50}
                                max={100}
                                value={serverSettings.episodeIntroClusterMinAgreementPct}
                                onChange={e => setServerSettings({ ...serverSettings, episodeIntroClusterMinAgreementPct: parseInt(e.target.value) || 70 })}
                                className="vora-input w-32"
                            />
                            <FieldHint>Episodes must agree within tolerance this often before the season median wins.</FieldHint>
                        </div>
                    </div>
                    <div>
                        <FieldLabel>Parallel items during Analyze</FieldLabel>
                        <input
                            type="number"
                            min={1}
                            max={16}
                            value={serverSettings.analyzeConcurrency}
                            onChange={e => setServerSettings({ ...serverSettings, analyzeConcurrency: parseInt(e.target.value) || 2 })}
                            className="vora-input w-32"
                        />
                        <FieldHint>How many media items an Analyze run decodes at once. Higher is faster but uses more CPU/GPU; raise it if you have hardware acceleration or spare cores.</FieldHint>
                    </div>
                </div>
            </SettingsCard>
            </>)}

            {subTab === 'thumbnails' && (<>
            <SettingsCard title="Video Preview Thumbnails">
                <div className="space-y-4">
                    <div>
                        <FieldLabel>Schedule Time</FieldLabel>
                        <input
                            type="time"
                            value={serverSettings.videoThumbnailScheduleTime}
                            onChange={e => setServerSettings({ ...serverSettings, videoThumbnailScheduleTime: e.target.value })}
                            className="vora-input w-40"
                        />
                        <FieldHint>Daily time the thumbnail generation pass runs. Only libraries with "Enable video preview thumbnails" turned on are processed.</FieldHint>
                    </div>
                    <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                        <div>
                            <FieldLabel>Interval (seconds)</FieldLabel>
                            <input
                                type="number"
                                min={2}
                                max={300}
                                value={serverSettings.videoThumbnailIntervalSeconds}
                                onChange={e => setServerSettings({ ...serverSettings, videoThumbnailIntervalSeconds: parseInt(e.target.value) || 10 })}
                                className="vora-input w-32"
                            />
                            <FieldHint>Seconds between captured frames. Lower = denser scrub-bar preview but bigger sprites.</FieldHint>
                        </div>
                        <div>
                            <FieldLabel>Width (px)</FieldLabel>
                            <input
                                type="number"
                                min={80}
                                max={1280}
                                value={serverSettings.videoThumbnailWidth}
                                onChange={e => setServerSettings({ ...serverSettings, videoThumbnailWidth: parseInt(e.target.value) || 320 })}
                                className="vora-input w-32"
                            />
                        </div>
                        <div>
                            <FieldLabel>Height (px)</FieldLabel>
                            <input
                                type="number"
                                min={45}
                                max={720}
                                value={serverSettings.videoThumbnailHeight}
                                onChange={e => setServerSettings({ ...serverSettings, videoThumbnailHeight: parseInt(e.target.value) || 180 })}
                                className="vora-input w-32"
                            />
                        </div>
                    </div>
                    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                        <div>
                            <FieldLabel>JPEG Quality</FieldLabel>
                            <input
                                type="number"
                                min={2}
                                max={31}
                                value={serverSettings.videoThumbnailJpegQuality}
                                onChange={e => setServerSettings({ ...serverSettings, videoThumbnailJpegQuality: parseInt(e.target.value) || 9 })}
                                className="vora-input w-32"
                            />
                            <FieldHint>FFmpeg quality scale (2 = best/largest, 31 = worst/smallest). Default 9 keeps sprites small without visible artifacts on a hover preview.</FieldHint>
                        </div>
                        <div>
                            <FieldLabel>Sprite Columns</FieldLabel>
                            <input
                                type="number"
                                min={1}
                                max={20}
                                value={serverSettings.videoThumbnailSpriteColumns}
                                onChange={e => setServerSettings({ ...serverSettings, videoThumbnailSpriteColumns: parseInt(e.target.value) || 10 })}
                                className="vora-input w-32"
                            />
                            <FieldHint>How many thumbnail tiles per row in the sprite sheet. Higher = wider sprites, fewer rows.</FieldHint>
                        </div>
                    </div>
                    <FieldHint>Changing any of these values invalidates existing sprites — the next scheduled pass regenerates affected items (locked items are skipped).</FieldHint>
                    <div>
                        <FieldLabel>Parallel items during generation</FieldLabel>
                        <input
                            type="number"
                            min={1}
                            max={16}
                            value={serverSettings.videoThumbnailConcurrency}
                            onChange={e => setServerSettings({ ...serverSettings, videoThumbnailConcurrency: parseInt(e.target.value) || 2 })}
                            className="vora-input w-32"
                        />
                        <FieldHint>How many videos generate thumbnails at once. Higher is faster but uses more CPU/GPU.</FieldHint>
                    </div>
                </div>
            </SettingsCard>
            </>)}

            {subTab === 'general' && (<>
            <SettingsCard title="Live TV & Radio Health Check">
                <div className="space-y-4">
                    <div>
                        <FieldLabel>Nightly Re-Check Time</FieldLabel>
                        <input
                            type="time"
                            value={serverSettings.iptvHealthCheckTime}
                            onChange={e => setServerSettings({ ...serverSettings, iptvHealthCheckTime: e.target.value })}
                            className="vora-input w-40"
                        />
                        <FieldHint>Daily time the channel health re-check runs. Only playlists with "Health-check channels" enabled are probed; dead channels are hidden from clients and recovered ones are automatically restored.</FieldHint>
                    </div>
                </div>
            </SettingsCard>
            </>)}

            {subTab === 'scanning' && (<>
            <SettingsCard title="Real-Time File Watcher">
                <div className="space-y-4">
                    <div>
                        <FieldLabel>Watcher Engine</FieldLabel>
                        <select
                            value={serverSettings.folderWatcherProviderId}
                            onChange={e => setServerSettings({ ...serverSettings, folderWatcherProviderId: e.target.value })}
                            className="vora-input max-w-md cursor-pointer"
                        >
                            <option value="native_watcher">Native OS Watcher (fast, local drives only)</option>
                            <option value="polling_watcher">Polling Watcher (universal, network/Docker safe)</option>
                        </select>
                    </div>
                    {serverSettings.folderWatcherProviderId === 'polling_watcher' && (
                        <div className="pt-2">
                            <FieldLabel>Polling Interval (seconds)</FieldLabel>
                            <input
                                type="number"
                                min={10}
                                value={serverSettings.folderWatcherPollingInterval}
                                onChange={e => setServerSettings({ ...serverSettings, folderWatcherPollingInterval: parseInt(e.target.value) || 30 })}
                                className="vora-input w-32"
                            />
                            <FieldHint>How often Vora should sweep the directories for changes.</FieldHint>
                        </div>
                    )}
                </div>
            </SettingsCard>
            </>)}

            <button type="submit" disabled={isSaving} className="vora-button-primary">
                {isSaving ? 'Saving…' : 'Save Core Settings'}
            </button>
            </form>
        </div>
    );
}
