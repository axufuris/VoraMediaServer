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
