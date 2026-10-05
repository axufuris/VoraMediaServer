import type { BackupSizeEstimateVM } from '../../../api/System/backupsService';

const GROUP_ORDER = ['Settings', 'Security', 'Templates', 'Library', 'Iptv', 'Discovery', 'Podcasts', 'UserData'];

const GROUP_LABELS: Record<string, string> = {
    Settings: 'Settings',
    Security: 'Security',
    Templates: 'Templates',
    Library: 'Library',
    Iptv: 'Live TV & DVR',
    Discovery: 'Discovery & Requests',
    Podcasts: 'Podcasts',
    UserData: 'User data'
};

export interface BackupSectionGroupView<T> {
    group: string;
    label: string;
    sections: T[];
}

export function backupGroupLabel(group: string): string {
    return GROUP_LABELS[group] ?? group;
}

export function groupBackupSections<T extends { group: string }>(sections: T[]): BackupSectionGroupView<T>[] {
    const byGroup = new Map<string, T[]>();
    sections.forEach(section => {
        const list = byGroup.get(section.group);
        if (list) list.push(section);
        else byGroup.set(section.group, [section]);
    });

    const rank = (group: string) => {
        const index = GROUP_ORDER.indexOf(group);
        return index === -1 ? GROUP_ORDER.length : index;
    };

    return [...byGroup.entries()]
        .sort(([a], [b]) => rank(a) - rank(b) || a.localeCompare(b))
        .map(([group, list]) => ({ group, label: backupGroupLabel(group), sections: list }));
}

const SIZE_UNITS = ['KB', 'MB', 'GB', 'TB'];

export function formatBackupSize(bytes: number): string {
    if (!Number.isFinite(bytes) || bytes < 1024) return `${Math.max(0, Math.round(bytes || 0))} B`;

    let value = bytes / 1024;
    let unit = 0;
    while (value >= 1024 && unit < SIZE_UNITS.length - 1) {
        value /= 1024;
        unit++;
    }

    const rounded = value >= 100 ? Math.round(value).toString() : value.toFixed(1).replace(/\.0$/, '');
    return `${rounded} ${SIZE_UNITS[unit]}`;
}

export function formatRowCount(rows: number): string {
    return `${rows.toLocaleString('en-US')} ${rows === 1 ? 'row' : 'rows'}`;
}

export interface BackupSizeTotal {
    bytes: number;
    rows: number;
    incomplete: boolean;
}

export function estimateBackupTotal(estimate: BackupSizeEstimateVM, includedKeys: ReadonlySet<string>): BackupSizeTotal {
    const included = estimate.sections.filter(s => includedKeys.has(s.key));
    return {
        bytes: included.length === 0 ? 0 : estimate.overheadBytes + included.reduce((sum, s) => sum + s.estimatedBytes, 0),
        rows: included.reduce((sum, s) => sum + s.rowCount, 0),
        incomplete: included.some(s => s.failed)
    };
}
