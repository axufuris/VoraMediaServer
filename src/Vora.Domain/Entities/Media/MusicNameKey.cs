using System.Text;

namespace Vora.Domain.Entities.Media;

// Matches a title from a provider to a title from a file tag. The two come from
// different people typing into different systems, so they disagree about case,
// spacing and punctuation that nobody would call a different song.
//
// Deliberately NOT normalised away: anything in brackets. "Kansas (piano
// version)" is a different recording from "Kansas", and folding the two together
// would hand the piano version the original single's popularity. Likewise
// "(Deluxe Edition)" — a missed match leaves a value null, which is honest; a
// wrong match puts another record's number on it, which is not.
public static class MusicNameKey
{
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var builder = new StringBuilder(name.Length);
        var lastWasSpace = false;

        foreach (var raw in name.Trim())
        {
            var c = Fold(raw);

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) builder.Append(' ');
                lastWasSpace = true;
                continue;
            }

            builder.Append(char.ToLowerInvariant(c));
            lastWasSpace = false;
        }

        return builder.ToString();
    }

    // Curly quotes and typographic dashes are the same keystroke to a person
    // and different characters to a comparison.
    private static char Fold(char c) => c switch
    {
        '‘' or '’' or '‛' or '′' or '`' => '\'',
        '“' or '”' or '″' => '"',
        '‐' or '‑' or '‒' or '–' or '—' or '−' => '-',
        ' ' => ' ',
        _ => c,
    };
}
