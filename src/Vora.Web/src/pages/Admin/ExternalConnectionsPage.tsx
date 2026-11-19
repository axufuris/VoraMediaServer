import { Navigate, useNavigate, useParams } from 'react-router-dom';
import WebhooksTab from '../../components/Admin/ExternalConnections/WebhooksTab';
import FeatureTabs from '../../components/Admin/Features/FeatureTabs';
import { useNoticeModal } from '../../components/Admin/Primitives/NoticeModal';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import RequestServersTab from '../../components/Admin/Settings/RequestServersTab';
import { resolveAdminPath } from '../../components/Admin/Shell/adminNavData';

type ExternalConnectionsTabKey = 'request-servers' | 'webhooks';

const TABS: { key: ExternalConnectionsTabKey; label: string }[] = [
    { key: 'request-servers', label: 'Request Servers' },
    { key: 'webhooks', label: 'Webhooks' },
];

const DEFAULT_TAB: ExternalConnectionsTabKey = 'request-servers';

const tabPath = (tab: ExternalConnectionsTabKey, serverId?: string) => resolveAdminPath(`/admin/external-connections/${tab}`, serverId);

const isTabKey = (value?: string): value is ExternalConnectionsTabKey => TABS.some(tab => tab.key === value);

/**
 * The services Vora talks to, in two tabs: the Request Servers it sends requests to and the
 * webhooks other apps send it. The tab is in the address (`/admin/external-connections/:tab`), so
 * another page can link straight to one. The bare path and an unknown tab open the default one.
 */
export default function ExternalConnectionsPage() {
    const { serverId, tab } = useParams<{ serverId?: string; tab?: string }>();
    const navigate = useNavigate();
    const { noticeModal, showModal } = useNoticeModal();

    if (!isTabKey(tab)) {
        return <Navigate to={tabPath(DEFAULT_TAB, serverId)} replace />;
    }

    return (
        <div data-vora-page="">
            <PageHeader
                title="External Connections"
                description="The services Vora talks to: where requests are sent, and the apps that tell Vora about new files."
            />

            <div className="px-8 max-md:px-4 pt-2 pb-10 max-w-6xl mx-auto">
                <FeatureTabs
                    tabs={TABS}
                    activeKey={tab}
                    onChange={key => { if (isTabKey(key)) navigate(tabPath(key, serverId)); }}
                />

                {tab === 'request-servers' && (
                    <RequestServersTab serverId={serverId} showModal={showModal} />
                )}
                {tab === 'webhooks' && (
                    <WebhooksTab serverId={serverId} />
                )}
            </div>

            {noticeModal}
        </div>
    );
}
