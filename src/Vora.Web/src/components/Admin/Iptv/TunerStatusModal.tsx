import { useEffect, useState } from 'react';
import { iptvAdminService, type HdHomeRunStatusVM, type HdHomeRunTunerVM } from '../../../api/Iptv/iptvAdminService';
import { Modal, ModalHeader } from '../../Common/Modal';
import HealthBadge from '../Primitives/HealthBadge';
import { describeDevice, describeTunerUse, formatBitrate, formatFrequency, signalTone, tunerChannel, type SignalTone } from './tunerStatus';

interface Props {
    isOpen: boolean;
    onClose: () => void;
    playlistId: string;
    playlistName: string;
    serverId?: string;
}

const REFRESH_MS = 5000;

const TONE_FILL: Record<SignalTone, string> = {
    good: 'bg-[var(--vora-success-500)]',
    fair: 'bg-[var(--vora-warning-500)]',
    poor: 'bg-[var(--vora-danger-500)]',
};

function SignalMeter({ label, percent }: { label: string; percent: number | null | undefined }) {
    if (percent === null || percent === undefined) return null;
    return (
        <div className="min-w-0">
            <div className="mb-1 flex items-baseline justify-between text-xs text-[var(--vora-text-muted)]">
                <span>{label}</span>
                <span className="tabular-nums text-[var(--vora-text-secondary)]">{percent}%</span>
            </div>
            <div role="meter" aria-label={label} aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent} className="h-1.5 overflow-hidden rounded-full bg-[var(--vora-bg-sunken)]">
                <div className={`h-full rounded-full ${TONE_FILL[signalTone(percent)]}`} style={{ width: `${percent}%` }} />
            </div>
        </div>
    );
}

function TunerRow({ tuner }: { tuner: HdHomeRunTunerVM }) {
    const channel = tunerChannel(tuner);
    const details = [
        tuner.frequencyMhz ? formatFrequency(tuner.frequencyMhz) : null,
        tuner.bitsPerSecond ? formatBitrate(tuner.bitsPerSecond) : null,
        tuner.targetAddress ? `to ${tuner.targetAddress}` : null,
    ].filter((part): part is string => Boolean(part));

    return (
        <li className="vora-card p-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="flex min-w-0 items-center gap-2">
                    <span className="text-sm font-semibold text-[var(--vora-text-primary)]">Tuner {tuner.index + 1}</span>
                    <HealthBadge tone={tuner.inUse ? 'info' : 'neutral'} showDot={false}>{tuner.inUse ? 'In use' : 'Idle'}</HealthBadge>
                </div>
                {channel && <span className="min-w-0 truncate text-sm text-[var(--vora-text-primary)]">{channel}</span>}
            </div>
            {tuner.inUse && (
                <>
                    {details.length > 0 && <p className="mt-1 text-xs text-[var(--vora-text-muted)]">{details.join(' · ')}</p>}
                    <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-3">
                        <SignalMeter label="Signal strength" percent={tuner.signalStrengthPercent} />
                        <SignalMeter label="Signal quality" percent={tuner.signalQualityPercent} />
                        <SignalMeter label="Symbol quality" percent={tuner.symbolQualityPercent} />
                    </div>
                </>
            )}
        </li>
    );
}

export default function TunerStatusModal({ isOpen, onClose, playlistId, playlistName, serverId }: Props) {
    const [status, setStatus] = useState<HdHomeRunStatusVM | null>(null);
    const [loadFailed, setLoadFailed] = useState(false);

    useEffect(() => {
        if (!isOpen) return;
        let cancelled = false;
        const poll = () => {
            iptvAdminService.getTunerStatus(playlistId, serverId)
                .then(result => {
                    if (cancelled) return;
                    setStatus(result);
                    setLoadFailed(false);
                })
                .catch(() => {
                    if (!cancelled) setLoadFailed(true);
                });
        };
        poll();
        const timer = window.setInterval(poll, REFRESH_MS);
        return () => {
            cancelled = true;
            window.clearInterval(timer);
        };
    }, [isOpen, playlistId, serverId]);

    return (
        <Modal isOpen={isOpen} onClose={onClose} size="2xl" zIndex="z-[200]" surface="light" closeOnBackdropClick overlayPadding="p-6" cardClassName="p-6 flex flex-col max-h-[90vh]">
            <ModalHeader
                title="Tuners"
                subtitle={<span className="text-[var(--vora-text-muted)] text-sm">{status?.deviceName ?? playlistName}</span>}
                onClose={onClose}
                bordered={false}
                surface="light"
            />
            <div className="mb-4 border-b border-[var(--vora-border-subtle)]" />

            <div className="flex-1 space-y-4 overflow-y-auto pr-2">
                {!status && !loadFailed && <p className="text-sm text-[var(--vora-text-muted)]">Asking the device…</p>}
                {loadFailed && (
                    <p role="alert" className="rounded-[var(--vora-radius-md)] border border-[var(--vora-danger-500)]/20 bg-[var(--vora-danger-soft)] p-3 text-sm text-[var(--vora-danger-text)]">
                        The server could not be asked for the tuner status.
                    </p>
                )}

                {status && !status.succeeded && (
                    <p role="alert" className="rounded-[var(--vora-radius-md)] border border-[var(--vora-danger-500)]/20 bg-[var(--vora-danger-soft)] p-3 text-sm text-[var(--vora-danger-text)]">
                        {status.error ?? 'The device could not be reached.'}
                    </p>
                )}

                {status?.succeeded && (
                    <>
                        <div>
                            <p className="text-sm text-[var(--vora-text-primary)]">{describeTunerUse(status)}</p>
                            {describeDevice(status) && <p className="mt-1 text-xs text-[var(--vora-text-muted)]">{describeDevice(status)}</p>}
                        </div>
                        {status.tuners.length === 0 ? (
                            <p className="text-sm text-[var(--vora-text-muted)]">The device did not list its tuners.</p>
                        ) : (
                            <ul className="space-y-3">
                                {status.tuners.map(tuner => <TunerRow key={tuner.index} tuner={tuner} />)}
                            </ul>
                        )}
                        <p className="text-xs text-[var(--vora-text-muted)]">
                            This is what the device reports right now, including every other app that uses it, and it refreshes every few seconds. A tuner that is in use and not by Vora is another app, or a recording the device made itself.
                        </p>
                    </>
                )}
            </div>
        </Modal>
    );
}
