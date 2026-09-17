namespace Vora.Plugins;

// Helpers for the server-wide metadata language. The value returned by
// IPluginSettingsProvider.GetMetadataLanguageAsync() is a TVDB-style ISO 639-2
// (3-letter) code such as "eng" — TVDB's own endpoints take it verbatim. Any
// metadata plugin whose API expects a different form should map it here rather
// than re-deriving the table, so a new language added to the admin dropdown
// lights up across every provider at once.
public static class MetadataLanguageCodes
{
    // ISO 639-2/T (3-letter, as stored) -> ISO 639-1 (2-letter). Covers exactly
    // the languages offered in the admin Metadata Language dropdown.
    private static readonly Dictionary<string, string> Iso6392ToIso6391 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eng"] = "en", ["spa"] = "es", ["fra"] = "fr", ["deu"] = "de", ["ita"] = "it",
        ["por"] = "pt", ["nld"] = "nl", ["jpn"] = "ja", ["kor"] = "ko", ["zho"] = "zh",
        ["rus"] = "ru", ["swe"] = "sv", ["pol"] = "pl", ["tur"] = "tr", ["dan"] = "da",
        ["nor"] = "no", ["fin"] = "fi", ["ara"] = "ar", ["heb"] = "he", ["hin"] = "hi",
        ["ces"] = "cs", ["ell"] = "el", ["hun"] = "hu", ["ukr"] = "uk", ["tha"] = "th",
    };

    // The stored 3-letter code as an ISO 639-1 (2-letter) code — the form TMDB
    // and most REST metadata APIs expect. Unknown codes fall back to English.
    public static string ToIso6391(string? iso6392) =>
        iso6392 != null && Iso6392ToIso6391.TryGetValue(iso6392, out var code) ? code : "en";
}
