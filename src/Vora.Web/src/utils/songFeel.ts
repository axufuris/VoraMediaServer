import type { TrackEnergy } from '../api/Music/musicService';

export interface SongFeelInput {
    moods?: string[];
    energy?: TrackEnergy | null;
    isInstrumental?: boolean;
    max?: number;
}

export function moodName(mood?: string): string {
    return mood ? mood.charAt(0).toUpperCase() + mood.slice(1) : '';
}

export function songFeelParts({ moods = [], energy, isInstrumental, max = 3 }: SongFeelInput): string[] {
    const parts = moods.slice(0, max).map(moodName);
    if (energy) parts.push(`${energy} energy`);
    if (isInstrumental) parts.push('Instrumental');
    return parts;
}
