import { useEffect, useState } from 'react';
import { versionService, type ServerVersionVM } from '../../../api/System/versionService';

// The build the server is actually running, shown where every admin page can
// see it — a bug report without it can't be tied to a release.
export default function ServerVersionBadge() {
    const [serverVersion, setServerVersion] = useState<ServerVersionVM | null>(null);

    useEffect(() => {
        let cancelled = false;
        versionService.getVersion()
            .then(v => { if (!cancelled) setServerVersion(v); })
            .catch(() => { /* a version we can't read just isn't shown */ });
        return () => { cancelled = true; };
    }, []);

    if (!serverVersion) return null;

    const title = serverVersion.commit
        ? `Vora ${serverVersion.version} (${serverVersion.commit.slice(0, 7)})`
        : `Vora ${serverVersion.version}`;

    return (
        <div className="flex items-center gap-1.5 px-3 pt-2 text-xs" style={{ color: 'var(--vora-text-muted)' }} title={title}>
            <span className="truncate">v{serverVersion.version}</span>
            {serverVersion.isPrerelease && (
                <span
                    className="rounded px-1.5 py-0.5 text-[0.625rem] font-bold uppercase tracking-widest"
                    style={{ background: 'var(--vora-accent-soft)', color: 'var(--vora-accent-text)' }}
                >
                    Beta
                </span>
            )}
        </div>
    );
}
