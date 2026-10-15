namespace Vora.Plugins.Providers.Local;

public static class AudioFileCodecs
{
    // TagLib's Properties.Description is prose ("MPEG Version 1 Audio, Layer 3
    // VBR"), and Track.AudioCodec is varchar(32). The VBR form is 33 characters,
    // so storing it raw threw 22001 and — because the ingest loop had no
    // isolation — killed the whole library scan at the first VBR mp3.
    //
    // Length was only half of it. Every consumer substring-matches a short codec
    // token (BadgeResolver picks mp3.png/flac.png; MediaDedupeManager scores
    // lossless vs lossy), and the prose form matches none of them, so even the
    // tracks that did fit got no audio badge and the wrong dedupe score. The
    // container is the reliable signal; the description only disambiguates the
    // two codecs that share the .m4a container.
    public static string? Of(string filePath, string? description)
    {
        var lossless = description != null
            && (description.Contains("lossless", StringComparison.OrdinalIgnoreCase)
                || description.Contains("alac", StringComparison.OrdinalIgnoreCase));

        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".mp3" => "mp3",
            ".flac" => "flac",
            ".m4a" => lossless ? "alac" : "aac",
            ".aac" => "aac",
            ".ogg" => "vorbis",
            ".opus" => "opus",
            ".wav" => "pcm",
            ".wma" => "wma",
            _ => Truncate(description, 32)
        };
    }

    private static string? Truncate(string? value, int maxLength) =>
        value != null && value.Length > maxLength ? value[..maxLength] : value;
}
