// ISO 3166-1 alpha-2 codes used to populate the radio playlist country filter.
// Station country codes (from Radio-Browser / M3U tvg-country) are alpha-2, so
// the admin filter matches on these. Display names come from Intl.DisplayNames
// with the raw code as a fallback.
export const COUNTRY_CODES: string[] = [
    'AD', 'AE', 'AF', 'AG', 'AI', 'AL', 'AM', 'AO', 'AR', 'AT', 'AU', 'AW', 'AZ',
    'BA', 'BB', 'BD', 'BE', 'BF', 'BG', 'BH', 'BI', 'BJ', 'BM', 'BN', 'BO', 'BR',
    'BS', 'BT', 'BW', 'BY', 'BZ', 'CA', 'CD', 'CF', 'CG', 'CH', 'CI', 'CL', 'CM',
    'CN', 'CO', 'CR', 'CU', 'CV', 'CY', 'CZ', 'DE', 'DJ', 'DK', 'DM', 'DO', 'DZ',
    'EC', 'EE', 'EG', 'ER', 'ES', 'ET', 'FI', 'FJ', 'FM', 'FO', 'FR', 'GA', 'GB',
    'GD', 'GE', 'GH', 'GI', 'GL', 'GM', 'GN', 'GQ', 'GR', 'GT', 'GW', 'GY', 'HK',
    'HN', 'HR', 'HT', 'HU', 'ID', 'IE', 'IL', 'IN', 'IQ', 'IR', 'IS', 'IT', 'JM',
    'JO', 'JP', 'KE', 'KG', 'KH', 'KI', 'KM', 'KN', 'KP', 'KR', 'KW', 'KY', 'KZ',
    'LA', 'LB', 'LC', 'LI', 'LK', 'LR', 'LS', 'LT', 'LU', 'LV', 'LY', 'MA', 'MC',
    'MD', 'ME', 'MG', 'MH', 'MK', 'ML', 'MM', 'MN', 'MO', 'MR', 'MT', 'MU', 'MV',
    'MW', 'MX', 'MY', 'MZ', 'NA', 'NE', 'NG', 'NI', 'NL', 'NO', 'NP', 'NR', 'NZ',
    'OM', 'PA', 'PE', 'PG', 'PH', 'PK', 'PL', 'PR', 'PT', 'PW', 'PY', 'QA', 'RO',
    'RS', 'RU', 'RW', 'SA', 'SB', 'SC', 'SD', 'SE', 'SG', 'SI', 'SK', 'SL', 'SM',
    'SN', 'SO', 'SR', 'SS', 'ST', 'SV', 'SY', 'SZ', 'TD', 'TG', 'TH', 'TJ', 'TL',
    'TM', 'TN', 'TO', 'TR', 'TT', 'TV', 'TW', 'TZ', 'UA', 'UG', 'UK', 'US', 'UY',
    'UZ', 'VA', 'VC', 'VE', 'VN', 'VU', 'WS', 'YE', 'ZA', 'ZM', 'ZW',
];

let regionNames: Intl.DisplayNames | null = null;
try {
    regionNames = new Intl.DisplayNames(['en'], { type: 'region' });
} catch {
    regionNames = null;
}

export function countryName(code: string): string {
    // "UK" is an M3U normalization of the ISO "GB"; Intl only knows GB.
    const lookup = code === 'UK' ? 'GB' : code;
    const name = regionNames?.of(lookup);
    return name && name !== lookup ? name : code;
}

export interface CountryOption {
    code: string;
    name: string;
}

export const COUNTRY_OPTIONS: CountryOption[] = COUNTRY_CODES
    .map(code => ({ code, name: countryName(code) }))
    .sort((a, b) => a.name.localeCompare(b.name));
