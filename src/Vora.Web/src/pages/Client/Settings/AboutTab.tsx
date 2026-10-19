import { StorageKeys } from '../../../utils/storageKeys';
import { useDesktopPlayback } from '../../../desktop/useDesktopPlayback';

export default function AboutTab() {
    const { bridge } = useDesktopPlayback();
    return (
        <div className="space-y-6">
            <section className="vora-card p-6">
                <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>About this client</h2>
                <p className="m-0 mb-4 text-sm" style={{ color: 'var(--vora-text-muted)' }}>Vora is a self-hosted media server. {bridge ? 'This is the client running in Vora Desktop.' : 'This is the client running in your browser.'}</p>
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                    <div className="rounded-md p-3" style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}>
                        <div className="text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Client build</div>
                        <div className="mt-1 text-sm" style={{ color: 'var(--vora-text-primary)' }}>{import.meta.env.MODE ?? 'unknown'}</div>
                    </div>
                    <div className="rounded-md p-3" style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}>
                        <div className="text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Device id</div>
                        <div className="mt-1 truncate text-sm" style={{ color: 'var(--vora-text-primary)' }} title={localStorage.getItem(StorageKeys.deviceId) ?? ''}>{localStorage.getItem(StorageKeys.deviceId) ?? '—'}</div>
                    </div>
                    {bridge && (
                        <div className="rounded-md p-3" style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}>
                            <div className="text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>App</div>
                            <div className="mt-1 text-sm" style={{ color: 'var(--vora-text-primary)' }}>Vora Desktop {bridge.shellVersion} ({bridge.channel})</div>
                        </div>
                    )}
                </div>
            </section>
        </div>
    );
}
