// The numeric LibraryType the create form works in, mapped to the name the API
// speaks. Enums cross the HTTP boundary as strings, so anything sent back to the
// server — a provider list narrowed to a library kind, for instance — needs the
// name rather than the number.
//
// Exhaustive on purpose. The previous mapping fell through to 'Movie' for
// anything it did not recognise, so a Live TV library would have been told it
// could use the film metadata providers. An unknown value now maps to undefined,
// which reads as "no kind stated" and returns the unfiltered list, rather than
// confidently claiming the wrong one.
export const LIBRARY_TYPE_NAMES: Record<number, string> = {
    1: 'Movie',
    2: 'TvShow',
    3: 'Music',
    4: 'HomeVideo',
    5: 'LiveTv',
};

export function libraryTypeToName(type: number): string | undefined {
    return LIBRARY_TYPE_NAMES[type];
}

// Mirrors LibraryCapabilities.HasVideoContent in Vora.Domain. Frames, embedded
// subtitle tracks, scrub-bar sprites and chapter markers all mean nothing
// without a video stream, so the admin sections for them are hidden rather than
// shown reporting zero.
//
// The API sends the type as its enum NAME, so this compares case-insensitively
// on that rather than on the numeric form the create page works in.
const VIDEO_BEARING_TYPES = ['movie', 'tvshow', 'homevideo'];

export function libraryHasVideoContent(typeName: string | undefined): boolean {
    return !!typeName && VIDEO_BEARING_TYPES.includes(typeName.toLowerCase());
}
