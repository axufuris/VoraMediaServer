import SearchBar from '../../components/Layout/SearchBar';

interface MainLayoutUserMenuProps {
    profileName: string;
    isProfileMenuOpen: boolean;
    isServerAdmin: boolean;
    onToggleMenu: () => void;
    onCloseMenu: () => void;
    onManageServers: () => void;
    onServerSettings: () => void;
    onClientSettings: () => void;
    onAccountSettings: () => void;
    onPlayHistory: () => void;
    onSwitchProfile: () => void;
    onSignOut: () => void;
}

export default function MainLayoutUserMenu({
    profileName,
    isProfileMenuOpen,
    isServerAdmin,
    onToggleMenu,
    onCloseMenu,
    onManageServers,
    onServerSettings,
    onClientSettings,
    onAccountSettings,
    onPlayHistory,
    onSwitchProfile,
    onSignOut,
}: MainLayoutUserMenuProps) {
    const menuItemStyle: React.CSSProperties = { color: 'var(--vora-text-primary)' };

    return (
        <header
            className="relative z-[100] flex h-16 w-full shrink-0 items-center justify-between px-8"
            style={{ background: 'var(--vora-bg-sunken)', borderBottom: '1px solid var(--vora-border-subtle)' }}
        >
            <div className="mr-8 flex-1">
                <SearchBar />
            </div>

            <div className="relative">
                <button
                    type="button"
                    onClick={onToggleMenu}
                    className="flex cursor-pointer items-center gap-2 rounded-full p-1.5 pr-3 transition-colors hover:bg-white/5"
                    style={{ background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)' }}
                >
                    <div
                        className="flex h-8 w-8 items-center justify-center rounded-full text-sm font-semibold uppercase shadow-inner"
                        style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)' }}
                    >
                        {profileName.charAt(0)}
                    </div>
                    <svg
                        className={`h-4 w-4 transition-transform ${isProfileMenuOpen ? 'rotate-180' : ''}`}
                        fill="currentColor"
                        viewBox="0 0 20 20"
                        style={{ color: 'var(--vora-text-muted)' }}
                    >
                        <path fillRule="evenodd" d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z" clipRule="evenodd" />
                    </svg>
                </button>

                {isProfileMenuOpen && (
                    <>
                        <div className="fixed inset-0 z-[105]" onClick={onCloseMenu} />
                        <div
                            className="absolute right-0 z-[110] mt-2 w-56 overflow-hidden rounded-xl py-1"
                            style={{
                                background: 'var(--vora-bg-raised)',
                                border: '1px solid var(--vora-border-strong)',
                                boxShadow: 'var(--vora-shadow-overlay)',
                            }}
                        >
                            <div
                                className="px-4 py-3"
                                style={{ borderBottom: '1px solid var(--vora-border-subtle)', background: 'color-mix(in srgb, var(--vora-bg-surface) 50%, transparent)' }}
                            >
                                <p className="m-0 truncate text-sm font-medium" style={{ color: 'var(--vora-text-primary)' }}>{profileName}</p>
                                <p className="m-0 truncate text-xs" style={{ color: 'var(--vora-text-muted)' }}>Client profile</p>
                            </div>

                            <button
                                type="button"
                                onClick={onManageServers}
                                className="flex w-full cursor-pointer items-center gap-2 px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5"
                                style={{ ...menuItemStyle, borderBottom: '1px solid var(--vora-border-subtle)' }}
                            >
                                <svg className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth={1.75} viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10" /></svg>
                                Manage servers
                            </button>

                            {isServerAdmin && (
                                <button type="button" onClick={onServerSettings} className="block w-full cursor-pointer px-4 py-2 text-left text-sm transition-colors hover:bg-white/5" style={menuItemStyle}>
                                    Server settings
                                </button>
                            )}

                            <button type="button" onClick={onClientSettings} className="block w-full cursor-pointer px-4 py-2 text-left text-sm transition-colors hover:bg-white/5" style={menuItemStyle}>
                                Client settings
                            </button>

                            <button type="button" onClick={onAccountSettings} className="block w-full cursor-pointer px-4 py-2 text-left text-sm transition-colors hover:bg-white/5" style={menuItemStyle}>
                                Account settings
                            </button>
                            <button
                                type="button"
                                onClick={onPlayHistory}
                                className="block w-full cursor-pointer px-4 py-2 text-left text-sm transition-colors hover:bg-white/5"
                                style={{ ...menuItemStyle, borderBottom: '1px solid var(--vora-border-subtle)' }}
                            >
                                Play history
                            </button>

                            <button type="button" onClick={onSwitchProfile} className="block w-full cursor-pointer px-4 py-2 text-left text-sm transition-colors hover:bg-white/5" style={menuItemStyle}>
                                Switch profile
                            </button>
                            <button
                                type="button"
                                onClick={onSignOut}
                                className="mt-1 block w-full cursor-pointer px-4 py-2.5 text-left text-sm font-medium transition-colors hover:bg-white/5"
                                style={{ color: 'var(--vora-danger-text)', borderTop: '1px solid var(--vora-border-subtle)' }}
                            >
                                Sign out of client
                            </button>
                        </div>
                    </>
                )}
            </div>
        </header>
    );
}
