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

    // ISO 639-2/B (the bibliographic codes FFmpeg writes into stream tags) ->
    // 639-2/T (what the admin dropdown stores). Only the languages that actually
    // differ between the two standards need an entry.
    private static readonly Dictionary<string, string> BibliographicToTerminological = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fre"] = "fra", ["ger"] = "deu", ["dut"] = "nld", ["chi"] = "zho", ["cze"] = "ces",
        ["gre"] = "ell", ["ice"] = "isl", ["per"] = "fas", ["rum"] = "ron", ["slo"] = "slk",
        ["alb"] = "sqi", ["arm"] = "hye", ["baq"] = "eus", ["bur"] = "mya", ["geo"] = "kat",
        ["mac"] = "mkd", ["may"] = "msa", ["wel"] = "cym", ["tib"] = "bod", ["mao"] = "mri",
    };

    // Some stream tags reach Vora as display names rather than codes — the
    // ffprobe reader rewrites "eng" as "English" before anything stores it — so
    // a name has to normalize as readily as a code does.
    private static readonly Dictionary<string, string> EnglishNameToIso6392 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["english"] = "eng", ["spanish"] = "spa", ["french"] = "fra", ["german"] = "deu",
        ["italian"] = "ita", ["portuguese"] = "por", ["dutch"] = "nld", ["japanese"] = "jpn",
        ["korean"] = "kor", ["chinese"] = "zho", ["mandarin"] = "zho", ["russian"] = "rus",
        ["swedish"] = "swe", ["polish"] = "pol", ["turkish"] = "tur", ["danish"] = "dan",
        ["norwegian"] = "nor", ["finnish"] = "fin", ["arabic"] = "ara", ["hebrew"] = "heb",
        ["hindi"] = "hin", ["czech"] = "ces", ["greek"] = "ell", ["hungarian"] = "hun",
        ["ukrainian"] = "ukr", ["thai"] = "tha",
    };

    // Track languages arrive in every shape a muxer or a file name can produce:
    // "en", "eng", "en-US", "pt_BR", "ENG", "English". This reduces one to the
    // 639-2/T code the server stores, so the two can be compared at all.
    public static string? Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;

        var trimmed = language.Trim();
        if (EnglishNameToIso6392.TryGetValue(trimmed, out var named)) return named;

        var bare = trimmed.Split('-', '_')[0].ToLowerInvariant();
        if (bare.Length == 0) return null;

        if (bare.Length == 2)
        {
            foreach (var (iso6392, iso6391) in Iso6392ToIso6391)
            {
                if (iso6391 == bare) return iso6392.ToLowerInvariant();
            }
            return bare;
        }

        return BibliographicToTerminological.TryGetValue(bare, out var terminological) ? terminological : bare;
    }

    // True only when both sides name the same language. An unknown or missing
    // code is never a match: "we could not tell" must not read as "it matches",
    // or a track with no language tag would suppress the very subtitle the
    // foreign-audio rule exists to turn on.
    public static bool SameLanguage(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        return a != null && b != null && a == b;
    }
}
