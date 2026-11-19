using Vora.Domain.Entities.Media;
using Vora.Plugins;

namespace Vora.Application.Posters;

// Whether a title's poster overlay draws the Multi-Audio tag: it has audio in two
// or more different languages (docs/artwork-image-cache.md, "Multi-Audio tag").
// Tracks are counted by language, not by number, so a film with a 5.1 and a 2.0
// mix of the same English track, or a commentary track in English, is not
// multi-audio. A track with no language, or one tagged as undetermined, says
// nothing about the title and counts for none.
public static class OverlayAudioLanguages
{
    private static readonly HashSet<string> Unknown = new(StringComparer.OrdinalIgnoreCase)
    {
        "und", "undetermined", "unknown", "zxx", "mis", "mul",
    };

    public static bool HasMultiple(IEnumerable<MediaAudioTrack> tracks)
    {
        string? first = null;
        foreach (var track in tracks)
        {
            var language = MetadataLanguageCodes.Normalize(track.Language);
            if (language is null || Unknown.Contains(language))
            {
                continue;
            }

            if (first is null)
            {
                first = language;
            }
            else if (!string.Equals(first, language, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
