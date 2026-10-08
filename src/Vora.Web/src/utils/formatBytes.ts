const UNITS = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'];

export function formatBytes(bytes: number): string {
    if (bytes <= 0) return '0 B';
    const i = Math.min(UNITS.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
    const value = bytes / Math.pow(1024, i);
    return `${value.toFixed(value >= 100 || i === 0 ? 0 : 1)} ${UNITS[i]}`;
}
