import { describe, expect, it } from 'vitest';
import type { BackupSizeEstimateVM } from '../../../api/System/backupsService';
import { backupGroupLabel, estimateBackupTotal, formatBackupSize, formatRowCount, groupBackupSections } from './backupSections';

describe('formatBackupSize', () => {
    it('reads like a person would say it', () => {
        expect(formatBackupSize(0)).toBe('0 B');
        expect(formatBackupSize(512)).toBe('512 B');
        expect(formatBackupSize(1024)).toBe('1 KB');
        expect(formatBackupSize(12.4 * 1024 * 1024)).toBe('12.4 MB');
        expect(formatBackupSize(250 * 1024 * 1024)).toBe('250 MB');
        expect(formatBackupSize(3.25 * 1024 * 1024 * 1024)).toBe('3.3 GB');
    });
});

describe('formatRowCount', () => {
    it('pluralises and groups thousands', () => {
        expect(formatRowCount(1)).toBe('1 row');
        expect(formatRowCount(12345)).toBe('12,345 rows');
    });
});

describe('groupBackupSections', () => {
    it('orders groups settings first and user data last with readable labels', () => {
        const groups = groupBackupSections([
            { key: 'users.profiles', group: 'UserData' },
            { key: 'podcasts.shows', group: 'Podcasts' },
            { key: 'settings.server', group: 'Settings' },
            { key: 'iptv.playlists', group: 'Iptv' },
            { key: 'users.playlists', group: 'UserData' }
        ]);

        expect(groups.map(g => g.label)).toEqual(['Settings', 'Live TV & DVR', 'Podcasts', 'User data']);
        expect(groups[3].sections.map(s => s.key)).toEqual(['users.profiles', 'users.playlists']);
    });

    it('keeps a group the page does not know about, after the known ones', () => {
        expect(backupGroupLabel('Something')).toBe('Something');
        expect(groupBackupSections([{ key: 'b', group: 'Something' }, { key: 'a', group: 'Settings' }]).map(g => g.group))
            .toEqual(['Settings', 'Something']);
    });
});

describe('estimateBackupTotal', () => {
    const estimate: BackupSizeEstimateVM = {
        estimatedAtUtc: '2026-10-05T12:00:00Z',
        overheadBytes: 1000,
        sections: [
            { key: 'settings.server', estimatedBytes: 2000, rowCount: 1, failed: false },
            { key: 'users.watch-history', estimatedBytes: 5_000_000, rowCount: 40_000, failed: false },
            { key: 'users.ratings', estimatedBytes: 0, rowCount: 0, failed: true }
        ]
    };

    it('adds the ticked sections to the fixed zip overhead', () => {
        expect(estimateBackupTotal(estimate, new Set(['settings.server', 'users.watch-history'])))
            .toEqual({ bytes: 5_003_000, rows: 40_001, incomplete: false });
    });

    it('is zero when nothing is ticked and flags sections it could not measure', () => {
        expect(estimateBackupTotal(estimate, new Set()).bytes).toBe(0);
        expect(estimateBackupTotal(estimate, new Set(['users.ratings'])).incomplete).toBe(true);
    });
});
