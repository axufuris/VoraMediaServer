import { useEffect, useState, useCallback } from 'react';
import { systemSettingsAdminService, type ServerSettings } from '../../../api/System/systemSettingsAdminService';
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

    const handleSaveCore = async (e: React.SyntheticEvent<HTMLFormElement>) => {
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
        <form onSubmit={handleSaveCore} className="space-y-6 pt-2">
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

            <SettingsCard title="New User Registration">
                <FieldLabel>Registration Mode</FieldLabel>
                <select
                    value={serverSettings.registrationMode || 1}
                    onChange={e => setServerSettings({ ...serverSettings, registrationMode: Number(e.target.value) })}
                    className="vora-input max-w-md cursor-pointer"
                >
                    <option value={0}>Disabled (no new users allowed)</option>
                    <option value={1}>Simple (open registration)</option>
                    <option value={2}>Secret Word (requires admin invite code)</option>
                </select>
                <FieldHint>Control how new users can create accounts on this server.</FieldHint>
            </SettingsCard>

            <SettingsCard title="Silence Detection Schedule">
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

            <button type="submit" disabled={isSaving} className="vora-button-primary">
                {isSaving ? 'Saving…' : 'Save Core Settings'}
            </button>
        </form>
    );
}
