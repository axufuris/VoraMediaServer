export const homePathFor = (serverId?: string): string => (serverId ? `/server/${serverId}` : '/');

export const isAtHome = (pathname: string, homePath: string): boolean =>
    (pathname.length > 1 ? pathname.replace(/\/+$/, '') : pathname) === homePath;
