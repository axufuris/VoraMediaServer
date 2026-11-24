export interface PlaybackChoice {
    value: number;
    label: string;
}

export const BITRATE_CHOICES: readonly PlaybackChoice[] = [
    { value: 0, label: 'Original (no limit)' },
    { value: 40, label: '40 Mbps' },
    { value: 20, label: '20 Mbps' },
    { value: 10, label: '10 Mbps' },
    { value: 4, label: '4 Mbps' },
];

export const RESOLUTION_CHOICES: readonly PlaybackChoice[] = [
    { value: 0, label: 'Original' },
    { value: 2160, label: '4K (2160p)' },
    { value: 1080, label: 'HD (1080p)' },
    { value: 720, label: 'HD (720p)' },
];
