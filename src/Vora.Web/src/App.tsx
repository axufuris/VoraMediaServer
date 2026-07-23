import { BrowserRouter, Routes, Route, Navigate, useParams, Outlet } from 'react-router-dom';
import { useEffect, lazy, Suspense, type ReactElement } from 'react';
import { serverVault } from './utils/serverVault';
import { StorageKeys, getProfileIdFromToken } from './utils/storageKeys';
import MainLayout from './layouts/MainLayout';
import AdminShell from './components/Admin/Shell/AdminShell';
import { PlayerProvider } from './contexts/PlayerContext';
import { DialogProvider } from './dialogs';
import { ThemeProvider } from './theme/ThemeProvider';
import { ClientTemplateProvider } from './theme/ClientTemplateProvider';

import SetupPage from './pages/Auth/SetupPage';
import LoginPage from './pages/Auth/LoginPage';
import RegisterPage from './pages/Auth/RegisterPage';
import ForgotPasswordPage from './pages/Auth/ForgotPasswordPage';
import ResetPasswordPage from './pages/Auth/ResetPasswordPage';
import ConfirmEmailPage from './pages/Auth/ConfirmEmailPage';
import ProfileSelectionPage from './pages/Profile/ProfileSelectionPage';

const ClientLibraryPage = lazy(() => import('./pages/Client/LibraryPage'));
const LibraryDashboard = lazy(() => import('./pages/Client/LibraryDashboard'));
const ManageLibrary = lazy(() => import('./pages/Admin/Libraries/ManageLibrary'));
const ClientMediaDetailsPage = lazy(() => import('./pages/Client/Media/MediaDetailsPage'));
const ClientCollectionDetailsPage = lazy(() => import('./pages/Client/Collections/CollectionDetailsPage'));
const ClientCollectionsPage = lazy(() => import('./pages/Client/Collections/CollectionsPage'));
const TaskDashboard = lazy(() => import('./pages/Admin/Tasks/TaskDashboard'));
const ClientActorDetailsPage = lazy(() => import('./pages/Client/Media/ActorDetailsPage'));
const ClientHomePage = lazy(() => import('./pages/Client/HomePage'));
const AdminSmartListsPage = lazy(() => import('./pages/Admin/SmartLists/SmartListsPage'));
const AdminPluginsPage = lazy(() => import('./pages/Admin/PluginsPage'));
const AdminSettingsPage = lazy(() => import('./pages/Admin/SettingsPage'));
const CreateLibrary = lazy(() => import('./pages/Admin/Libraries/CreateLibrary'));
const AccountSettingsPage = lazy(() => import('./pages/Profile/AccountSettingsPage'));
const AdminUserManagementPage = lazy(() => import('./pages/Admin/UserManagementPage'));
const AdminInvitationsPage = lazy(() => import('./pages/Admin/InvitationsPage'));
const AuthorizedDevicesPage = lazy(() => import('./pages/Admin/AuthorizedDevicesPage'));
const AdminDashboardPage = lazy(() => import('./pages/Admin/DashboardPage'));
const AdminAppearancePage = lazy(() => import('./pages/Admin/AppearancePage'));
const AdminTemplateSchedulesPage = lazy(() => import('./pages/Admin/Templates/SchedulesPage'));
const AdminHistoryPage = lazy(() => import('./pages/Admin/HistoryPage'));
const ProfileHistoryPage = lazy(() => import('./pages/Client/ProfileHistoryPage'));
const ClientPlaylistsPage = lazy(() => import('./pages/Client/Playlists/PlaylistsPage'));
const ClientPlaylistDetailsPage = lazy(() => import('./pages/Client/Playlists/PlaylistDetailsPage'));
const SmartPlaylistDetailsPage = lazy(() => import('./pages/Client/Playlists/SmartPlaylistDetailsPage'));
const SearchPage = lazy(() => import('./pages/Client/SearchPage'));
const AdminDiscoveryPage = lazy(() => import('./pages/Admin/Discovery/DiscoveryPage'));
const ClientDiscoveryDetailsPage = lazy(() => import('./pages/Client/Discovery/DiscoveryDetailsPage'));
const ClientDiscoveryActorPage = lazy(() => import('./pages/Client/Discovery/DiscoveryActorPage'));
const ClientYouTubePage = lazy(() => import('./pages/Client/YouTube/YouTubePage'));
const ClientYouTubeChannelPage = lazy(() => import('./pages/Client/YouTube/YouTubeChannelPage'));
const ClientYouTubePlayerPage = lazy(() => import('./pages/Client/YouTube/YouTubePlayerPage'));
const ClientYouTubeSubscriptionsPage = lazy(() => import('./pages/Client/YouTube/YouTubeSubscriptionsPage'));
const AdminYouTubePage = lazy(() => import('./pages/Admin/Features/YouTubeAdminPage'));
const ClientDiscoveryViewAllPage = lazy(() => import('./pages/Client/Discovery/DiscoveryViewAllPage'));
const AdminRequestsPage = lazy(() => import('./pages/Admin/RequestsPage'));
const AdminAiStatsPage = lazy(() => import('./pages/Admin/AiStatsPage'));
const AdminDedupePage = lazy(() => import('./pages/Admin/DedupePage'));
const OverlayEditor = lazy(() => import('./pages/Admin/Overlay/OverlayEditor'));
const AdminIptvPage = lazy(() => import('./pages/Admin/Iptv/IptvPage'));
const AdminForYouPage = lazy(() => import('./pages/Admin/Features/ForYouPage'));
const AdminReleaseCalendarPage = lazy(() => import('./pages/Admin/Features/ReleaseCalendarPage'));
const AdminDvrPage = lazy(() => import('./pages/Admin/Features/DvrPage'));
const AdminCollectionsPage = lazy(() => import('./pages/Admin/Features/CollectionsAdminPage'));
const AdminMusicPage = lazy(() => import('./pages/Admin/Features/MusicAdminPage'));
const AdminLiveTvPage = () => <AdminIptvPage kind="Tv" />;
const AdminInternetRadioPage = () => <AdminIptvPage kind="Radio" />;
const AdminPodcastsPage = lazy(() => import('./pages/Admin/Podcasts/PodcastsAdminPage'));
const AdminMusicHistoryPage = lazy(() => import('./pages/Admin/MusicHistoryPage'));
const AdminLogsPage = lazy(() => import('./pages/Admin/LogsPage'));
const AdminBackupsPage = lazy(() => import('./pages/Admin/BackupsPage'));
const AdminLibraryMigrationPage = lazy(() => import('./pages/Admin/LibraryMigrationPage'));
const ClientSettingsPage = lazy(() => import('./pages/Client/SettingsPage'));
const MusicPage = lazy(() => import('./pages/Client/Audio/MusicPage'));
const PodcastsPage = lazy(() => import('./pages/Client/Audio/PodcastsPage'));
const RadioPage = lazy(() => import('./pages/Client/Audio/RadioPage'));
const DiscoverHubPage = lazy(() => import('./pages/Client/Discovery/DiscoverHubPage'));
const LiveTvHubPage = lazy(() => import('./pages/Client/LiveTv/LiveTvHubPage'));

if (!localStorage.getItem(StorageKeys.deviceId)) {
    localStorage.setItem(StorageKeys.deviceId, crypto.randomUUID());
}

if (serverVault.getServers().length === 0 && localStorage.getItem(StorageKeys.profileToken)) {
    try {
        const token = localStorage.getItem(StorageKeys.profileToken);
        const profileId = token ? getProfileIdFromToken(token) : null;
        if (!token || !profileId) throw new Error('Missing profile token for legacy migration');
        const isAdmin = localStorage.getItem(StorageKeys.isServerAdmin) === 'true';

        const apiUrl = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000/api';
        const baseUrl = apiUrl.endsWith('/api') ? apiUrl.slice(0, -4) : apiUrl;

        serverVault.addOrUpdateServer({
            id: 'legacy_server_migration',
            name: 'Vora Server',
            url: baseUrl,
            token: token,
            profileId: profileId,
            isAdmin: isAdmin
        });
        serverVault.setActiveServerId('legacy_server_migration');
    } catch (e) {
        console.error("Failed to migrate legacy auth data", e);
    }
}

const ServerContextWrapper = () => {
    const { serverId } = useParams<{ serverId: string }>();

    useEffect(() => {
        if (serverId && serverVault.getActiveServerId() !== serverId) {
            serverVault.setActiveServerId(serverId);
        }
    }, [serverId]);

    return <Outlet />;
};

const RequireAuth = ({ children }: { children: ReactElement }) => {
    const servers = serverVault.getServers();
    const activeServer = serverVault.getActiveServer();

    useEffect(() => {
        if (!activeServer && servers.length > 0) {
            serverVault.setActiveServerId(servers[0].id);
        }
    }, [activeServer, servers]);

    if (servers.length === 0) {
        return <Navigate to="/login" replace />;
    }

    const profileToken = localStorage.getItem(StorageKeys.profileToken);
    if (!profileToken) {
        return <Navigate to="/profiles" replace />;
    }

    return children;
};

const RequireAdmin = ({ children }: { children: ReactElement }) => {
    const isServerAdmin = localStorage.getItem(StorageKeys.isServerAdmin) === 'true';
    if (!isServerAdmin) {
        return <Navigate to="/" replace />;
    }
    return children;
};

export default function App() {
    return (
        <DialogProvider>
            <PlayerProvider>
                <BrowserRouter>
                    <ThemeProvider>
                        <ClientTemplateProvider>
                        <Suspense fallback={<div className="fixed inset-0 flex items-center justify-center" style={{ background: 'var(--vora-bg-canvas)' }}><div className="vora-skeleton h-12 w-48" /></div>}>
                        <Routes>
                    <Route path="/setup" element={<SetupPage />} />
                    <Route path="/login" element={<LoginPage />} />
                    <Route path="/register" element={<RegisterPage />} />
                    <Route path="/forgot-password" element={<ForgotPasswordPage />} />
                    <Route path="/reset-password" element={<ResetPasswordPage />} />
                    <Route path="/confirm-email" element={<ConfirmEmailPage />} />
                    <Route path="/profiles" element={<ProfileSelectionPage />} />

                    {/* CLIENT ROUTES */}
                    <Route path="/" element={<RequireAuth><MainLayout /></RequireAuth>}>
                        <Route index element={<ClientHomePage />} />
                        <Route path="account" element={<AccountSettingsPage />} />
                        <Route path="history" element={<ProfileHistoryPage />} />
                        <Route path="search" element={<SearchPage />} />

                        <Route path="library/:id" element={<ClientLibraryPage />} />
                        <Route path="playlists" element={<ClientPlaylistsPage />} />
                        <Route path="playlist/:id" element={<ClientPlaylistDetailsPage />} />
                        <Route path="smart-playlist/:id" element={<SmartPlaylistDetailsPage />} />
                        <Route path="media/:id" element={<ClientMediaDetailsPage />} />
                        <Route path="collection/:id" element={<ClientCollectionDetailsPage />} />
                        <Route path="collections" element={<ClientCollectionsPage />} />
                        <Route path="/actor/:id" element={<ClientActorDetailsPage />} />
                        <Route path="discovery" element={<DiscoverHubPage />} />
                        <Route path="livetv" element={<LiveTvHubPage />} />
                        <Route path="music" element={<MusicPage />} />
                        <Route path="podcasts" element={<PodcastsPage />} />
                        <Route path="radio" element={<RadioPage />} />
                        <Route path="settings" element={<ClientSettingsPage />} />
                        <Route path="discovery/:providerId/row/:rowId" element={<ClientDiscoveryViewAllPage />} />
                        <Route path="discovery/:providerId/:type/:externalId" element={<ClientDiscoveryDetailsPage />} />
                        <Route path="discovery/:providerId/actor/:externalId" element={<ClientDiscoveryActorPage />} />
                        <Route path="youtube" element={<ClientYouTubePage />} />
                        <Route path="youtube/subscriptions" element={<ClientYouTubeSubscriptionsPage />} />
                        <Route path="youtube/channel/:channelId" element={<ClientYouTubeChannelPage />} />
                        <Route path="youtube/watch/:videoId" element={<ClientYouTubePlayerPage />} />

                        <Route path="server/:serverId" element={<ServerContextWrapper />}>
                            <Route path="search" element={<SearchPage />} />
                            <Route path="library/:id" element={<ClientLibraryPage />} />
                            <Route path="media/:id" element={<ClientMediaDetailsPage />} />
                            <Route path="collection/:id" element={<ClientCollectionDetailsPage />} />
                            <Route path="collections" element={<ClientCollectionsPage />} />
                            <Route path="actor/:id" element={<ClientActorDetailsPage />} />
                            <Route path="playlists" element={<ClientPlaylistsPage />} />
                            <Route path="playlist/:id" element={<ClientPlaylistDetailsPage />} />
                            <Route path="smart-playlist/:id" element={<SmartPlaylistDetailsPage />} />
                            <Route path="discovery" element={<DiscoverHubPage />} />
                            <Route path="livetv" element={<LiveTvHubPage />} />
                            <Route path="music" element={<MusicPage />} />
                            <Route path="podcasts" element={<PodcastsPage />} />
                            <Route path="radio" element={<RadioPage />} />
                            <Route path="settings" element={<ClientSettingsPage />} />
                            <Route path="discovery/:providerId/row/:rowId" element={<ClientDiscoveryViewAllPage />} />
                            <Route path="discovery/:providerId/:type/:externalId" element={<ClientDiscoveryDetailsPage />} />
                            <Route path="discovery/:providerId/actor/:externalId" element={<ClientDiscoveryActorPage />} />
                            <Route path="youtube" element={<ClientYouTubePage />} />
                            <Route path="youtube/subscriptions" element={<ClientYouTubeSubscriptionsPage />} />
                            <Route path="youtube/channel/:channelId" element={<ClientYouTubeChannelPage />} />
                            <Route path="youtube/watch/:videoId" element={<ClientYouTubePlayerPage />} />
                        </Route>
                    </Route>

                    {/* ADMIN ROUTES */}
                    <Route path="/admin" element={<RequireAuth><RequireAdmin><AdminShell /></RequireAdmin></RequireAuth>}>
                        <Route index element={<AdminDashboardPage />} />
                        <Route path="history" element={<AdminHistoryPage />} />
                        <Route path="libraries" element={<LibraryDashboard />} />
                        <Route path="dedupe" element={<AdminDedupePage />} />
                        <Route path="devices" element={<AuthorizedDevicesPage />} />
                        <Route path="users" element={<AdminUserManagementPage />} />
                        <Route path="invitations" element={<AdminInvitationsPage />} />
                        <Route path="libraries/:id/manage" element={<ManageLibrary />} />
                        <Route path="libraries/new" element={<CreateLibrary />} />
                        <Route path="tasks" element={<TaskDashboard />} />
                        <Route path="smart-lists" element={<AdminSmartListsPage />} />
                        <Route path="plugins" element={<AdminPluginsPage />} />
                        <Route path="settings" element={<AdminSettingsPage />} />
                        <Route path="discovery" element={<AdminDiscoveryPage />} />
                        <Route path="for-you" element={<AdminForYouPage />} />
                        <Route path="release-calendar" element={<AdminReleaseCalendarPage />} />
                        <Route path="dvr-settings" element={<AdminDvrPage />} />
                        <Route path="collections" element={<AdminCollectionsPage />} />
                        <Route path="music" element={<AdminMusicPage />} />
                        <Route path="youtube" element={<AdminYouTubePage />} />
                        <Route path="requests" element={<AdminRequestsPage />} />
                        <Route path="ai-stats" element={<AdminAiStatsPage />} />
                        <Route path="overlays" element={<OverlayEditor />} />
                        <Route path="live-tv" element={<AdminLiveTvPage />} />
                        <Route path="internet-radio" element={<AdminInternetRadioPage />} />
                        <Route path="podcasts" element={<AdminPodcastsPage />} />
                        <Route path="music-history" element={<AdminMusicHistoryPage />} />
                        <Route path="appearance" element={<AdminAppearancePage />} />
                        <Route path="client-templates" element={<AdminTemplateSchedulesPage />} />
                        <Route path="logs" element={<AdminLogsPage />} />
                        <Route path="backups" element={<AdminBackupsPage />} />
                        <Route path="library-migration" element={<AdminLibraryMigrationPage />} />

                        <Route path="server/:serverId" element={<ServerContextWrapper />}>
                            <Route index element={<AdminDashboardPage />} />
                            <Route path="history" element={<AdminHistoryPage />} />
                            <Route path="libraries" element={<LibraryDashboard />} />
                            <Route path="dedupe" element={<AdminDedupePage />} />
                            <Route path="devices" element={<AuthorizedDevicesPage />} />
                            <Route path="users" element={<AdminUserManagementPage />} />
                            <Route path="invitations" element={<AdminInvitationsPage />} />
                            <Route path="libraries/:id/manage" element={<ManageLibrary />} />
                            <Route path="libraries/new" element={<CreateLibrary />} />
                            <Route path="tasks" element={<TaskDashboard />} />
                            <Route path="smart-lists" element={<AdminSmartListsPage />} />
                            <Route path="plugins" element={<AdminPluginsPage />} />
                            <Route path="settings" element={<AdminSettingsPage />} />
                            <Route path="discovery" element={<AdminDiscoveryPage />} />
                        <Route path="for-you" element={<AdminForYouPage />} />
                        <Route path="release-calendar" element={<AdminReleaseCalendarPage />} />
                        <Route path="dvr-settings" element={<AdminDvrPage />} />
                        <Route path="collections" element={<AdminCollectionsPage />} />
                        <Route path="music" element={<AdminMusicPage />} />
                        <Route path="youtube" element={<AdminYouTubePage />} />
                            <Route path="requests" element={<AdminRequestsPage />} />
                            <Route path="ai-stats" element={<AdminAiStatsPage />} />
                            <Route path="overlays" element={<OverlayEditor />} />
                            <Route path="live-tv" element={<AdminLiveTvPage />} />
                        <Route path="internet-radio" element={<AdminInternetRadioPage />} />
                        <Route path="podcasts" element={<AdminPodcastsPage />} />
                        <Route path="music-history" element={<AdminMusicHistoryPage />} />
                            <Route path="appearance" element={<AdminAppearancePage />} />
                            <Route path="client-templates" element={<AdminTemplateSchedulesPage />} />
                            <Route path="logs" element={<AdminLogsPage />} />
                            <Route path="backups" element={<AdminBackupsPage />} />
                            <Route path="library-migration" element={<AdminLibraryMigrationPage />} />
                        </Route>
                    </Route>

                        <Route path="*" element={<Navigate to="/" replace />} />
                        </Routes>
                        </Suspense>
                        </ClientTemplateProvider>
                    </ThemeProvider>
                </BrowserRouter>
            </PlayerProvider>
        </DialogProvider>
    );
}
