import { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { aiSettingsService, type AiStatus } from '../../api/System/aiSettingsService';
import AiSettingsForm from '../../components/Admin/Ai/AiSettingsForm';
import HealthBadge from '../../components/Admin/Primitives/HealthBadge';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import { resolveAdminPath } from '../../components/Admin/Shell/adminNavData';

function StatusLine({ status }: { status: AiStatus | null }) {
    if (!status) return null;
    if (!status.configured) {
        return <HealthBadge tone="neutral" showDot={false}>Not set up</HealthBadge>;
    }
    const where = status.isOpenAi ? 'OpenAI' : status.host;
    return status.embeddings
        ? <HealthBadge tone="ok">Using {where}</HealthBadge>
        : <HealthBadge tone="warn">Using {where}, without recommendations or AI playlists</HealthBadge>;
}

export default function AiSettingsPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const [status, setStatus] = useState<AiStatus | null>(null);

    const loadStatus = useCallback(() => {
        aiSettingsService.getStatus(serverId).then(setStatus).catch(() => setStatus(null));
    }, [serverId]);

    useEffect(() => { loadStatus(); }, [loadStatus]);

    return (
        <div data-vora-page="">
            <PageHeader
                title="AI Settings"
                description="Where Vora's AI features send their requests, and which model does each job: OpenAI, another service, or a model you run yourself."
                actions={
                    <Link to={resolveAdminPath('/admin/ai-stats', serverId)} className="vora-button-secondary">Usage &amp; stats</Link>
                }
            />

            <div className="mx-auto max-w-4xl space-y-6 p-8 max-md:p-4">
                <section className="vora-card flex flex-col gap-5 p-5">
                    <div className="flex flex-wrap items-start justify-between gap-3">
                        <div className="min-w-0">
                            <h2 className="text-[15px] font-semibold text-[var(--vora-text-primary)]">Connection and models</h2>
                            <p className="mt-0.5 text-[13px] text-[var(--vora-text-muted)]">Used by AI collections and story order, the "For You" recommendations and AI playlists.</p>
                        </div>
                        <StatusLine status={status} />
                    </div>
                    <AiSettingsForm serverId={serverId} idPrefix="ai-settings" onSaved={loadStatus} />
                </section>
            </div>
        </div>
    );
}
