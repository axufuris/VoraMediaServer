import { BrowserRouter, Routes, Route, Navigate, useParams, Outlet } from 'react-router-dom';
import { useEffect, type ReactElement } from 'react';
import { serverVault } from './utils/serverVault';
import MainLayout from './layouts/MainLayout';
import AdminShell from './components/Admin/Shell/AdminShell';
import ClientLibraryPage from './pages/Client/LibraryPage';
import LibraryDashboard from './pages/Client/LibraryDashboard';
import ManageLibrary from './pages/Admin/Libraries/ManageLibrary';
import ClientMediaDetailsPage from './pages/Client/Media/MediaDetailsPage';
import ClientCollectionDetailsPage from './pages/Client/Collections/CollectionDetailsPage';
import ClientCollectionsPage from './pages/Client/Collections/CollectionsPage';
import TaskDashboard from './pages/Admin/Tasks/TaskDashboard';
import ClientActorDetailsPage from './pages/Client/Media/ActorDetailsPage';
import ClientHomePage from './pages/Client/HomePage';
import AdminSmartListsPage from './pages/Admin/SmartLists/SmartListsPage';
import AdminPluginsPage from './pages/Admin/PluginsPage';
import AdminSettingsPage from './pages/Admin/SettingsPage';
import CreateLibrary from './pages/Admin/Libraries/CreateLibrary';
import RegisterPage from './pages/Auth/RegisterPage';
import AccountSettingsPage from './pages/Profile/AccountSettingsPage';
import AdminUserManagementPage from './pages/Admin/UserManagementPage';
import AdminInvitationsPage from './pages/Admin/InvitationsPage';
import AuthorizedDevicesPage from './pages/Admin/AuthorizedDevicesPage';
import { PlayerProvider } from './contexts/PlayerContext';
import { DialogProvider } from './dialogs';
import { ThemeProvider } from './theme/ThemeProvider';
import { ClientTemplateProvider } from './theme/ClientTemplateProvider';
import AdminDashboardPage from './pages/Admin/DashboardPage';
import AdminAppearancePage from './pages/Admin/AppearancePage';
import AdminTemplateSchedulesPage from './pages/Admin/Templates/SchedulesPage';
import AdminHistoryPage from './pages/Admin/HistoryPage';
import ProfileHistoryPage from './pages/Client/ProfileHistoryPage';
import SetupPage from './pages/Auth/SetupPage';
import LoginPage from './pages/Auth/LoginPage';
import ForgotPasswordPage from './pages/Auth/ForgotPasswordPage';
import ResetPasswordPage from './pages/Auth/ResetPasswordPage';
import ProfileSelectionPage from './pages/Profile/ProfileSelectionPage';
import ClientPlaylistsPage from './pages/Client/Playlists/PlaylistsPage';
import ClientPlaylistDetailsPage from './pages/Client/Playlists/PlaylistDetailsPage';
import SmartPlaylistDetailsPage from './pages/Client/Playlists/SmartPlaylistDetailsPage';
import SearchPage from './pages/Client/SearchPage';
import AdminDiscoveryPage from './pages/Admin/Discovery/DiscoveryPage';
import ClientDiscoveryPage from './pages/Client/Discovery/DiscoveryPage';
import ClientDiscoveryDetailsPage from './pages/Client/Discovery/DiscoveryDetailsPage';
import ClientDiscoveryActorPage from './pages/Client/Discovery/DiscoveryActorPage';
import ClientWatchlistPage from './pages/Client/WatchlistPage';
import ClientDiscoveryViewAllPage from './pages/Client/Discovery/DiscoveryViewAllPage';
import AdminRequestsPage from './pages/Admin/RequestsPage';
import CalendarPage from './pages/Client/CalendarPage';
import ClientRecommendationsPage from './pages/Client/RecommendationsPage';
import AdminAiStatsPage from './pages/Admin/AiStatsPage';
import AdminDedupePage from './pages/Admin/DedupePage';
import OverlayEditor from './pages/Admin/Overlay/OverlayEditor';
import AdminIptvPage from './pages/Admin/Iptv/IptvPage';
import AdminForYouPage from './pages/Admin/Features/ForYouPage';
import AdminReleaseCalendarPage from './pages/Admin/Features/ReleaseCalendarPage';
import AdminDvrPage from './pages/Admin/Features/DvrPage';
import AdminCollectionsPage from './pages/Admin/Features/CollectionsAdminPage';
import AdminMusicPage from './pages/Admin/Features/MusicAdminPage';
const AdminLiveTvPage = () => <AdminIptvPage kind="Tv" />;
const AdminInternetRadioPage = () => <AdminIptvPage kind="Radio" />;
import AdminPodcastsPage from './pages/Admin/Podcasts/PodcastsAdminPage';
import AdminMusicHistoryPage from './pages/Admin/MusicHistoryPage';
import AdminLogsPage from './pages/Admin/LogsPage';
import AdminBackupsPage from './pages/Admin/BackupsPage';
import AdminLibraryMigrationPage from './pages/Admin/LibraryMigrationPage';
import LiveTvPage from './pages/Client/LiveTv/LiveTvPage';
import ClientSettingsPage from './pages/Client/SettingsPage';
import DvrDashboard from './pages/Client/LiveTv/DvrDashboard'; // <-- NEW
import AudioHubPage from './pages/Client/Audio/AudioHubPage';

if (!localStorage.getItem('device_id')) {
    localStorage.setItem('device_id', crypto.randomUUID());
}

if (serverVault.getServers().length === 0 && localStorage.getItem('profile_token')) {
    try {
        const token = localStorage.getItem('profile_token')!;
        const profileId = JSON.parse(atob(token.split('.')[1])).sub;
        const isAdmin = localStorage.getItem('is_server_admin') === 'true';

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
    let activeServer = serverVault.getActiveServer();

    if (servers.length === 0) {
        return <Navigate to="/login" replace />;
    }

    if (!activeServer && servers.length > 0) {
        serverVault.setActiveServerId(servers[0].id);
        activeServer = servers[0];
    }

    const profileToken = localStorage.getItem('profile_token');
    if (!profileToken) {
        return <Navigate to="/profiles" replace />;
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
                        <Routes>
                    <Route path="/setup" element={<SetupPage />} />
                    <Route path="/login" element={<LoginPage />} />
                    <Route path="/register" element={<RegisterPage />} />
                    <Route path="/forgot-password" element={<ForgotPasswordPage />} />
                    <Route path="/reset-password" element={<ResetPasswordPage />} />
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
                        <Route path="discovery" element={<ClientDiscoveryPage />} />
                        <Route path="recommendations" element={<ClientRecommendationsPage />} />
                        <Route path="watchlist" element={<ClientWatchlistPage />} />
                        <Route path="calendar" element={<CalendarPage />} />
                        <Route path="livetv" element={<LiveTvPage />} />
                        <Route path="dvr" element={<DvrDashboard />} /> {/* <-- NEW */}
                        <Route path="audio" element={<AudioHubPage />} />
                        <Route path="settings" element={<ClientSettingsPage />} />
                        <Route path="discovery/:providerId/row/:rowId" element={<ClientDiscoveryViewAllPage />} />
                        <Route path="discovery/:providerId/:type/:externalId" element={<ClientDiscoveryDetailsPage />} />
                        <Route path="discovery/:providerId/actor/:externalId" element={<ClientDiscoveryActorPage />} />

                        <Route path="server/:serverId" element={<ServerContextWrapper />}>
                            <Route path="search" element={<SearchPage />} />
                            <Route path="library/:id" element={<ClientLibraryPage />} />
                            <Route path="media/:id" element={<ClientMediaDetailsPage />} />
                            <Route path="collection/:id" element={<ClientCollectionDetailsPage />} />
                            <Route path="collections" element={<ClientCollectionsPage />} />
                            <Route path="actor/:id" element={<ClientActorDetailsPage />} />
                            <Route path="playlists" element={<ClientPlaylistsPage />} />
                            <Route path="watchlist" element={<ClientWatchlistPage />} />
                            <Route path="playlist/:id" element={<ClientPlaylistDetailsPage />} />
                            <Route path="smart-playlist/:id" element={<SmartPlaylistDetailsPage />} />
                            <Route path="discovery" element={<ClientDiscoveryPage />} />
                            <Route path="recommendations" element={<ClientRecommendationsPage />} />
                            <Route path="calendar" element={<CalendarPage />} />
                            <Route path="livetv" element={<LiveTvPage />} />
                            <Route path="dvr" element={<DvrDashboard />} /> {/* <-- NEW */}
                            <Route path="audio" element={<AudioHubPage />} />
                            <Route path="settings" element={<ClientSettingsPage />} />
                            <Route path="discovery/:providerId/row/:rowId" element={<ClientDiscoveryViewAllPage />} />
                            <Route path="discovery/:providerId/:type/:externalId" element={<ClientDiscoveryDetailsPage />} />
                            <Route path="discovery/:providerId/actor/:externalId" element={<ClientDiscoveryActorPage />} />
                        </Route>
                    </Route>

                    {/* ADMIN ROUTES */}
                    <Route path="/admin" element={<RequireAuth><AdminShell /></RequireAuth>}>
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
                        </ClientTemplateProvider>
                    </ThemeProvider>
                </BrowserRouter>
            </PlayerProvider>
        </DialogProvider>
    );
}
