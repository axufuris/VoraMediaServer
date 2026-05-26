namespace Vora.Plugins.Dtos;

public enum LibraryKind
{
    Movie,
    TvShow,
    Music,
    HomeVideo,
    Photo,
    Podcast,
    YouTube,
    Audiobook
}

public static class LibraryKindExtensions
{
    public static bool TryParseLibraryKind(string value, out LibraryKind kind)
    {
        kind = LibraryKind.Movie;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return Enum.TryParse(value, ignoreCase: true, out kind);
    }
}
