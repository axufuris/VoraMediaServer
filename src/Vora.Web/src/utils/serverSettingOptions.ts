export const SCHEDULE_TIME_ZONES = [
    'America/New_York', 'America/Chicago', 'America/Denver', 'America/Phoenix',
    'America/Los_Angeles', 'America/Anchorage', 'Pacific/Honolulu',
    'America/Toronto', 'America/Vancouver', 'America/Sao_Paulo', 'America/Mexico_City',
    'Europe/London', 'Europe/Dublin', 'Europe/Lisbon', 'Europe/Madrid', 'Europe/Paris',
    'Europe/Amsterdam', 'Europe/Brussels', 'Europe/Berlin', 'Europe/Zurich', 'Europe/Rome',
    'Europe/Stockholm', 'Europe/Oslo', 'Europe/Copenhagen', 'Europe/Helsinki',
    'Europe/Warsaw', 'Europe/Prague', 'Europe/Athens', 'Europe/Bucharest',
    'Europe/Kyiv', 'Europe/Moscow', 'Europe/Istanbul',
    'Asia/Jerusalem', 'Asia/Dubai', 'Asia/Karachi', 'Asia/Kolkata', 'Asia/Bangkok',
    'Asia/Singapore', 'Asia/Hong_Kong', 'Asia/Shanghai', 'Asia/Tokyo', 'Asia/Seoul',
    'Australia/Perth', 'Australia/Adelaide', 'Australia/Brisbane', 'Australia/Sydney',
    'Pacific/Auckland', 'Africa/Johannesburg', 'Africa/Lagos', 'Africa/Cairo', 'UTC',
];

export const METADATA_LANGUAGES: ReadonlyArray<readonly [string, string]> = [
    ['eng', 'English'], ['spa', 'Spanish (Español)'], ['fra', 'French (Français)'], ['deu', 'German (Deutsch)'],
    ['ita', 'Italian (Italiano)'], ['por', 'Portuguese (Português)'], ['nld', 'Dutch (Nederlands)'], ['swe', 'Swedish (Svenska)'],
    ['dan', 'Danish (Dansk)'], ['nor', 'Norwegian (Norsk)'], ['fin', 'Finnish (Suomi)'], ['pol', 'Polish (Polski)'],
    ['ces', 'Czech (Čeština)'], ['ell', 'Greek (Ελληνικά)'], ['hun', 'Hungarian (Magyar)'], ['tur', 'Turkish (Türkçe)'],
    ['rus', 'Russian (Русский)'], ['ukr', 'Ukrainian (Українська)'], ['ara', 'Arabic (العربية)'], ['heb', 'Hebrew (עברית)'],
    ['hin', 'Hindi (हिन्दी)'], ['tha', 'Thai (ไทย)'], ['jpn', 'Japanese (日本語)'], ['kor', 'Korean (한국어)'], ['zho', 'Chinese (中文)'],
];

export function browserTimeZone(): string | null {
    try {
        return Intl.DateTimeFormat().resolvedOptions().timeZone || null;
    } catch {
        return null;
    }
}
