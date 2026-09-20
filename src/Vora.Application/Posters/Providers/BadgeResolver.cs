using Vora.Plugins.Dtos;

namespace Vora.Application.Posters.Providers;

internal static class BadgeResolver
{
    private const int RottenTomatoesFreshThreshold = 60;

    public static string? DetermineBadgePath(OverlayMediaDto item, string type, string basePath) => type switch
    {
        "resolution" => GetResolutionBadge(item.Resolution, item.VideoFormat, basePath),
        "content_rating" => GetContentRatingBadge(item.ContentRating, basePath),
        "audio_codec" => GetAudioBadge(item.AudioCodec, basePath),
        "stinger" => item.HasStinger ? Path.Combine(basePath, "MediaStingers", "MediaStinger.png") : null,
        "edition" => GetEditionBadge(item.Edition, basePath),
        _ => null
    };

    public static string? GetPlatformLogoPath(string? platformName, decimal? score, string basePath)
    {
        if (string.IsNullOrWhiteSpace(platformName))
        {
            return null;
        }

        var name = platformName.Trim().ToLower();
        var fileName = ResolveRatingPlatformFile(name, score);
        return fileName != null ? Path.Combine(basePath, "Rating", fileName) : null;
    }

    public static bool IsRottenTomatoesLogo(string imagePath) =>
        imagePath.Contains("RT-", StringComparison.OrdinalIgnoreCase)
        || imagePath.Contains("rotten", StringComparison.OrdinalIgnoreCase);

    public static bool IsVoraStarLogo(string imagePath) =>
        imagePath.EndsWith("Star.png", StringComparison.OrdinalIgnoreCase);

    private static string? GetResolutionBadge(string? resolution, string? hdrType, string basePath)
    {
        var resBase = string.Empty;
        if (!string.IsNullOrWhiteSpace(resolution))
        {
            var r = resolution.ToLower();
            if (r.Contains("4k") || r == "2160p") resBase = "4k";
            else if (r.Contains("1080")) resBase = "1080p";
            else if (r.Contains("720")) resBase = "720p";
            else if (r.Contains("576")) resBase = "576p";
            else if (r.Contains("480") || r == "sd") resBase = "480p";
        }

        var mod = ResolveHdrModifier(hdrType);
        if (string.IsNullOrEmpty(resBase) && string.IsNullOrEmpty(mod))
        {
            return null;
        }

        return Path.Combine(basePath, "Resolution", $"{resBase}{mod}.png");
    }

    private static string ResolveHdrModifier(string? hdrType)
    {
        if (string.IsNullOrWhiteSpace(hdrType))
        {
            return string.Empty;
        }

        var h = hdrType.ToLower();
        var hasDv = h.Contains("dovi") || h.Contains("dolby vision") || h.Contains("vision");
        var hasHdr10Plus = h.Contains("hdr10+") || h.Contains("hdr10plus");
        var hasHdr = h.Contains("hdr") && !hasHdr10Plus;
        var hasHlg = h.Contains("hlg");

        if (hasDv && hasHdr10Plus) return "dvhdrplus";
        if (hasDv && hasHdr) return "dvhdr";
        if (hasDv) return "dv";
        if (hasHdr10Plus) return "plus";
        if (hasHdr) return "hdr";
        if (hasHlg) return "hlg";
        return string.Empty;
    }

    private static string? GetContentRatingBadge(string? rating, string basePath)
    {
        if (string.IsNullOrWhiteSpace(rating))
        {
            return null;
        }

        var fileName = rating.Trim().ToLower() switch
        {
            "g" => "usg.png",
            "pg" => "uspg.png",
            "pg-13" or "pg13" => "uspg-13.png",
            "r" => "usr.png",
            "nc-17" or "nc17" => "usnc-17.png",
            "nr" or "not rated" or "unrated" => "usnr.png",
            "tv-y" or "tvy" => "ustv-y.png",
            "tv-g" or "tvg" => "ustv-g.png",
            "tv-pg" or "tvpg" => "ustv-pg.png",
            "tv-14" or "tv14" => "ustv-14.png",
            "tv-ma" or "tvma" => "ustv-ma.png",
            _ => null
        };

        return fileName != null ? Path.Combine(basePath, "ContentRating", fileName) : null;
    }

    private static string? GetAudioBadge(string? codec, string basePath)
    {
        if (string.IsNullOrWhiteSpace(codec))
        {
            return null;
        }

        var clean = codec.ToLower();

        string? fileName = null;
        if (clean.Contains("atmos")) fileName = "atmos.png";
        else if (clean.Contains("dtshd") || clean.Contains("dts-hd") || clean.Contains("dts hd")) fileName = "hra.png";
        else if (clean.Contains("dtsx") || clean.Contains("dts:x")) fileName = "dtsx.png";
        else if (clean.Contains("dts") && !clean.Contains("hd")) fileName = "dts.png";
        else if (clean.Contains("truehd")) fileName = "truehd.png";
        else if (clean.Contains("dolby") || clean == "ac3" || clean == "eac3") fileName = "digital.png";
        else if (clean.Contains("flac")) fileName = "flac.png";
        else if (clean.Contains("aac")) fileName = "aac.png";
        else if (clean.Contains("mp3")) fileName = "mp3.png";
        else if (clean.Contains("opus")) fileName = "opus.png";
        else if (clean.Contains("pcm")) fileName = "pcm.png";

        return fileName != null ? Path.Combine(basePath, "AudioCodec", fileName) : null;
    }

    private static string? GetEditionBadge(string? edition, string basePath)
    {
        if (string.IsNullOrWhiteSpace(edition))
        {
            return null;
        }

        var safeEdition = edition.Trim().ToLower();
        var fileName = safeEdition switch
        {
            var e when e.Contains("alternate") => "alternate.png",
            var e when e.Contains("anniversary") => "anniversary.png",
            var e when e.Contains("black") && e.Contains("chrome") => "blackchrome.png",
            var e when e.Contains("coda") => "coda.png",
            var e when e.Contains("collector") => "collector.png",
            var e when e.Contains("criterion") => "criterion.png",
            var e when e.Contains("definitive") => "definitive.png",
            var e when e.Contains("diamond") => "diamond.png",
            var e when e.Contains("director") => "directors.png",
            var e when e.Contains("imax enhanced") => "enhanced.png",
            var e when e.Contains("extended") => "extended.png",
            var e when e.Contains("final cut") => "final.png",
            var e when e.Contains("imax") => "imax.png",
            var e when e.Contains("international") => "international.png",
            var e when e.Contains("open matte") => "openmatte.png",
            var e when e.Contains("platinum") => "platinum.png",
            var e when e.Contains("producer") => "producers.png",
            var e when e.Contains("remaster") => "remastered.png",
            var e when e.Contains("richard donner") => "richarddonner.png",
            var e when e.Contains("special") => "special.png",
            var e when e.Contains("theatrical") => "theatrical.png",
            var e when e.Contains("ultimate") => "ultimate.png",
            var e when e.Contains("ulysses") => "ulysses.png",
            var e when e.Contains("uncut") => "uncut.png",
            var e when e.Contains("unrated") => "unrated.png",
            _ => null
        };

        return fileName != null ? Path.Combine(basePath, "Edition", fileName) : null;
    }

    private static string? ResolveRatingPlatformFile(string name, decimal? score)
    {
        if (name == "rotten tomatoes audience" || name == "rt audience")
        {
            return IsFresh(score) ? "RT-Aud-Fresh.png" : "RT-Aud-Rotten.png";
        }

        if (name == "rotten tomatoes critic"
            || name == "rt critic"
            || name == "rotten tomatoes"
            || name == "rotten tomatoes certified"
            || name == "rt certified")
        {
            return IsFresh(score) ? "RT-Crit-Fresh.png" : "RT-Crit-Rotten.png";
        }

        return name switch
        {
            "anidb" => "AniDB.png",
            "imdb" or "internet movie database" => "IMDb.png",
            "imdb top" => "IMDbTop.png",
            "imdb top 100" => "IMDbTop100.png",
            "imdb top 250" => "IMDbTop250.png",
            "imdb top 1000" => "IMDbTop1000.png",
            "letterboxd" => "Letterboxd.png",
            "mal" or "myanimelist" => "MAL.png",
            "mdblist" => "MDBList.png",
            "metacritic" => "Metacritic.png",
            "metacritic must-see" => "MetacriticTop.png",
            "star" or "vora" => "Star.png",
            "tmdb" or "the movie database" => "TMDb.png",
            "trakt" => "Trakt.png",
            _ => null
        };
    }

    private static bool IsFresh(decimal? score) =>
        score.HasValue && score.Value >= RottenTomatoesFreshThreshold;
}
