import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { setupGuideService, type SetupGuideVM } from '../../../api/System/setupGuideService';
import { resolveAdminPath } from '../Shell/adminNavData';

export default function SetupGuideBanner({ serverId }: { serverId?: string }) {
    const [guide, setGuide] = useState<SetupGuideVM | null>(null);

    useEffect(() => {
        let cancelled = false;
        setupGuideService.get(serverId).then(g => { if (!cancelled) setGuide(g); }).catch(() => { });
        return () => { cancelled = true; };
    }, [serverId]);

    if (!guide || guide.status === 'Completed' || guide.status === 'Skipped') return null;

    return (
        <section className="flex flex-wrap items-center justify-between gap-4 rounded-[var(--vora-radius-lg)] border border-[color-mix(in_srgb,var(--vora-accent-500)_45%,transparent)] bg-[var(--vora-accent-soft)] px-5 py-4">
            <div className="min-w-0">
                <h2 className="font-semibold text-[var(--vora-text-primary)]">{guide.status === 'InProgress' ? 'Finish setting up Vora' : 'Set up your server'}</h2>
                <p className="text-sm text-[var(--vora-text-secondary)]">
                    {guide.status === 'InProgress' ? 'Your answers so far are saved. Pick up where you left off.' : 'A short guide sets up playback and connects the services that fetch posters, details and ratings.'}
                </p>
            </div>
            <Link to={resolveAdminPath('/admin/setup', serverId)} className="vora-button-primary">{guide.status === 'InProgress' ? 'Continue setup' : 'Start setup'}</Link>
        </section>
    );
}
