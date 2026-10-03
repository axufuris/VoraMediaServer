import type { TrackEnergy } from '../api/Music/musicService';

export interface SongFeelInput {
    moods?: string[];
    energy?: TrackEnergy | null;
    isInstrumental?: boolean;
    max?: number;
}

export function songFeelParts({ moods = [], energy, isInstrumental, max = 3 }: SongFeelInput): string[] {
    const parts = moods.slice(0, max).map(m => m.charAt(0).toUpperCase() + m.slice(1));
    if (energy) parts.push(`${energy} energy`);
    if (isInstrumental) parts.push('Instrumental');
    return parts;
}
