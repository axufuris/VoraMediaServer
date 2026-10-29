const Example = ({ children }: { children: string }) => <span className="text-[var(--vora-text-secondary)]">{children}</span>;

// What to type for the AI content providers, under the field they describe.
export default function ContentProviderHelp({ providerId }: { providerId: string }) {
    if (providerId === 'openai_list') {
        return (
            <p className="text-xs text-[var(--vora-text-muted)] leading-relaxed mt-2">
                Name a specific franchise or shared universe — e.g. <Example>Marvel Cinematic Universe</Example>, <Example>DC Extended Universe</Example>, or <Example>Star Wars</Example>. Every movie and season is pulled in automatically, so the universe name alone is enough (no need to add movies and shows, and don&apos;t narrow it to films only). For a holiday or occasion, use AI Theme; for a genre or mood like all your kung-fu movies, use a Smart Collection instead.
            </p>
        );
    }

    if (providerId === 'openai_theme') {
        return (
            <p className="text-xs text-[var(--vora-text-muted)] leading-relaxed mt-2">
                Name the occasion or theme — e.g. <Example>Halloween</Example>, <Example>Valentine&apos;s Day</Example>, <Example>Mother&apos;s Day</Example>, or <Example>Christmas</Example>. Vora adds the movies and shows in your library that fit it: titles TMDB tags with the theme go in directly, and the titles whose descriptions come closest are checked once by the AI. Its answers are kept, so later syncs only ask about new titles, and a title you remove stays out. Set Seasonal Visibility to show it only around the occasion.
            </p>
        );
    }

    return null;
}
