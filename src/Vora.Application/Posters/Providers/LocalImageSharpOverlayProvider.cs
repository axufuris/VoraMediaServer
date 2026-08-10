using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Vora.Application.Posters.Dtos;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Posters.Providers;

public class LocalImageSharpOverlayProvider(ILogger<LocalImageSharpOverlayProvider> logger) : IOverlayProvider
{
    private const string CustomArtworkUrlPrefix = "/api/artwork/custom/";
    private const string CompositeRatingsType = "composite_ratings";
    private const float BackgroundOpacity = 0.65f;
    private const float BezierCircleKappa = 0.5522847498f;
    private static readonly byte BackgroundAlphaByte = (byte)(BackgroundOpacity * 255f);
    private const float CompositeGapPct = 0.04f;
    private const float CompositeFontHeightPct = 0.35f;
    private const float CompositeFontWidthPct = 0.45f;
    private const float CompositeLogoHeightPct = 0.40f;
    private const float CompositeLogoTopPct = 0.10f;
    private const float CompositeTextYPct = 0.75f;
    private const int CompositeMaxItems = 3;
    private const int MaxCanvasWidthPx = 1280;
    private const int OverlayJpegQuality = 90;
    private const double PosterAspect = 2.0 / 3.0;

    private static readonly JsonSerializerOptions ConfigurationParseOptions = new() { PropertyNameCaseInsensitive = true };

    public string Id => "local_imagesharp_overlays";
    public string Name => "Vora Native Overlays";
    public string Version => "1.0.0";
    public string Description => "Natively generates poster overlays using SixLabors.ImageSharp.";
    public bool IsSystemPlugin => true;
    public string Type => "OverlayEngine";

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => new List<PluginSettingDefinitionDto>
    {
        new() { Key = "enable_schedule", Label = "Enable Nightly Generation", Type = "boolean", DefaultValue = "false" },
        new() { Key = "schedule_time", Label = "Schedule Time", Type = "time", DefaultValue = "03:00" }
    };

    public async Task<string> GenerateOverlayAsync(OverlayMediaDto item, string originalArtworkPath, string templateJson, string outputDirectory, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(originalArtworkPath))
        {
            return originalArtworkPath;
        }

        var layoutElements = JsonSerializer.Deserialize<List<OverlayElementDto>>(templateJson, ConfigurationParseOptions);
        if (layoutElements == null || layoutElements.Count == 0)
        {
            return originalArtworkPath;
        }

        var cacheKey = ComputeOverlayCacheKey(item, originalArtworkPath, templateJson);
        var outputFileName = $"{item.Id}_overlay_{cacheKey}.jpg";
        var outputPath = System.IO.Path.Combine(outputDirectory, outputFileName);

        if (File.Exists(outputPath))
        {
            return $"{CustomArtworkUrlPrefix}{outputFileName}";
        }

        using var baseImage = await Image.LoadAsync<Rgba32>(originalArtworkPath, cancellationToken);

        NormalizeToPosterAspect(baseImage);

        if (baseImage.Width > MaxCanvasWidthPx)
        {
            var scale = (double)MaxCanvasWidthPx / baseImage.Width;
            var newHeight = (int)Math.Round(baseImage.Height * scale);
            baseImage.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(MaxCanvasWidthPx, newHeight),
                Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.Lanczos3
            }));
        }

        var basePath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Overlays");

        foreach (var element in layoutElements)
        {
            if (string.IsNullOrWhiteSpace(element.Type))
            {
                continue;
            }

            if (element.Type == CompositeRatingsType)
            {
                await DrawCompositeRatingsAsync(baseImage, item, element, basePath, cancellationToken);
                continue;
            }

            await DrawBadgeElementAsync(baseImage, item, element, basePath, cancellationToken);
        }

        var encoder = new JpegEncoder { Quality = OverlayJpegQuality };

        await baseImage.SaveAsJpegAsync(outputPath, encoder, cancellationToken);
        return $"{CustomArtworkUrlPrefix}{outputFileName}";
    }

    // Clients render posters at a 2:3 aspect with object-cover (center crop).
    // Badges are placed as percentages of the base image, so on source art that
    // isn't 2:3 they land where the client then crops — clipped off the side or
    // overlapping other UI. Center-crop the base to 2:3 first so the baked
    // overlay matches exactly what the client shows.
    private static void NormalizeToPosterAspect(Image<Rgba32> image)
    {
        var currentAspect = (double)image.Width / image.Height;
        if (Math.Abs(currentAspect - PosterAspect) < 0.001)
        {
            return;
        }

        int cropWidth, cropHeight;
        if (currentAspect > PosterAspect)
        {
            cropHeight = image.Height;
            cropWidth = (int)Math.Round(image.Height * PosterAspect);
        }
        else
        {
            cropWidth = image.Width;
            cropHeight = (int)Math.Round(image.Width / PosterAspect);
        }

        var cropX = Math.Max(0, (image.Width - cropWidth) / 2);
        var cropY = Math.Max(0, (image.Height - cropHeight) / 2);
        image.Mutate(x => x.Crop(new Rectangle(cropX, cropY, cropWidth, cropHeight)));
    }

    private const string CacheKeyVersion = "v6-poster-aspect-2x3";

    private static string ComputeOverlayCacheKey(OverlayMediaDto item, string originalArtworkPath, string templateJson)
    {
        var sourceInfo = new FileInfo(originalArtworkPath);

        var builder = new StringBuilder();
        builder.Append(CacheKeyVersion).Append('|');
        builder.Append(templateJson).Append('|');
        builder.Append(sourceInfo.Length).Append('|');
        builder.Append(sourceInfo.LastWriteTimeUtc.Ticks).Append('|');
        builder.Append(item.Resolution).Append('|');
        builder.Append(item.VideoFormat).Append('|');
        builder.Append(item.AudioCodec).Append('|');
        builder.Append(item.ContentRating).Append('|');
        builder.Append(item.HasStinger).Append('|');
        builder.Append(item.Edition).Append('|');
        builder.Append(item.ServerAdminRating).Append('|');
        builder.Append(item.ThirdPartyRating1Name).Append('|');
        builder.Append(item.ThirdPartyRating1).Append('|');
        builder.Append(item.ThirdPartyRating2Name).Append('|');
        builder.Append(item.ThirdPartyRating2);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    private async Task DrawBadgeElementAsync(Image<Rgba32> baseImage, OverlayMediaDto item, OverlayElementDto element, string basePath, CancellationToken cancellationToken = default)
    {
        var badgePath = BadgeResolver.DetermineBadgePath(item, element.Type, basePath);
        if (string.IsNullOrEmpty(badgePath) || !File.Exists(badgePath))
        {
            return;
        }

        using var badgeImage = await Image.LoadAsync<Rgba32>(badgePath, cancellationToken);

        var targetWidth = Math.Max(1, (int)(baseImage.Width * element.WidthPct));
        var targetHeight = Math.Max(1, (int)(baseImage.Height * element.HeightPct));
        var targetX = (int)(baseImage.Width * element.XPct);
        var targetY = (int)(baseImage.Height * element.YPct);

        var padding = (int)Math.Max(5, targetWidth * 0.1f);
        var radius = padding * 0.8f;
        var maxBadgeW = Math.Max(1, targetWidth - (padding * 2));
        var maxBadgeH = Math.Max(1, targetHeight - (padding * 2));

        badgeImage.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(maxBadgeW, maxBadgeH),
            Mode = ResizeMode.Max,
            Sampler = KnownResamplers.Lanczos3
        }));

        var finalX = targetX + padding + ((maxBadgeW - badgeImage.Width) / 2);
        var finalY = targetY + padding + ((maxBadgeH - badgeImage.Height) / 2);

        baseImage.Mutate(x =>
        {
            DrawRoundedBackground(x, targetX, targetY, targetWidth, targetHeight, radius);
            x.DrawImage(badgeImage, new Point(finalX, finalY), 1f);
        });
    }

    private static void DrawRoundedBackground(IImageProcessingContext context, float x, float y, float w, float h, float r)
    {
        if (w <= 0f || h <= 0f)
        {
            return;
        }

        if (r * 2f > w) r = w / 2f;
        if (r * 2f > h) r = h / 2f;

        var fillColor = Color.FromRgba(0, 0, 0, BackgroundAlphaByte);

        if (r <= 0f)
        {
            context.Fill(fillColor, new RectangularPolygon(x, y, w, h));
            return;
        }

        var k1 = r * (1f - BezierCircleKappa);

        var topLineStart = new PointF(x + r, y);
        var topLineEnd = new PointF(x + w - r, y);
        var rightLineStart = new PointF(x + w, y + r);
        var rightLineEnd = new PointF(x + w, y + h - r);
        var bottomLineStart = new PointF(x + w - r, y + h);
        var bottomLineEnd = new PointF(x + r, y + h);
        var leftLineStart = new PointF(x, y + h - r);
        var leftLineEnd = new PointF(x, y + r);

        var path = new PathBuilder()
            .AddLine(topLineStart, topLineEnd)
            .AddCubicBezier(topLineEnd, new PointF(x + w - k1, y), new PointF(x + w, y + k1), rightLineStart)
            .AddLine(rightLineStart, rightLineEnd)
            .AddCubicBezier(rightLineEnd, new PointF(x + w, y + h - k1), new PointF(x + w - k1, y + h), bottomLineStart)
            .AddLine(bottomLineStart, bottomLineEnd)
            .AddCubicBezier(bottomLineEnd, new PointF(x + k1, y + h), new PointF(x, y + h - k1), leftLineStart)
            .AddLine(leftLineStart, leftLineEnd)
            .AddCubicBezier(leftLineEnd, new PointF(x, y + k1), new PointF(x + k1, y), topLineStart)
            .CloseFigure()
            .Build();

        context.Fill(fillColor, path);
    }

    private async Task DrawCompositeRatingsAsync(Image<Rgba32> baseImage, OverlayMediaDto item, OverlayElementDto element, string basePath, CancellationToken cancellationToken = default)
    {
        var validRatings = ResolveValidRatings(item, basePath);
        if (validRatings.Count == 0)
        {
            return;
        }

        var containerWidth = Math.Max(1, (int)(baseImage.Width * element.WidthPct));
        var containerHeight = Math.Max(1, (int)(baseImage.Height * element.HeightPct));
        var targetX = (int)(baseImage.Width * element.XPct);
        var targetY = (int)(baseImage.Height * element.YPct);

        var gap = (int)Math.Max(2, containerHeight * CompositeGapPct);
        var boxHeight = (containerHeight - (gap * (CompositeMaxItems - 1))) / CompositeMaxItems;
        var radius = containerWidth * 0.1f;

        var padding = (int)Math.Max(4, containerWidth * 0.1f);
        var innerWidth = Math.Max(1, containerWidth - (padding * 2));

        var font = TryLoadCompositeFont(boxHeight, innerWidth);

        for (var i = 0; i < validRatings.Count; i++)
        {
            await DrawCompositeRatingRowAsync(baseImage, validRatings[i], targetX, targetY, containerWidth, boxHeight, gap, radius, innerWidth, i, font, cancellationToken);
        }
    }

    private static List<(string ImagePath, decimal Score)> ResolveValidRatings(OverlayMediaDto item, string basePath)
    {
        var path1 = BadgeResolver.GetPlatformLogoPath("Vora", item.ServerAdminRating, basePath);
        var path2 = BadgeResolver.GetPlatformLogoPath(item.ThirdPartyRating1Name, item.ThirdPartyRating1, basePath);
        var path3 = BadgeResolver.GetPlatformLogoPath(item.ThirdPartyRating2Name, item.ThirdPartyRating2, basePath);

        var validRatings = new List<(string ImagePath, decimal Score)>();

        if (!string.IsNullOrEmpty(path1) && File.Exists(path1) && item.ServerAdminRating.HasValue)
            validRatings.Add((path1, item.ServerAdminRating.Value));

        if (!string.IsNullOrEmpty(path2) && File.Exists(path2) && item.ThirdPartyRating1.HasValue)
            validRatings.Add((path2, item.ThirdPartyRating1.Value));

        if (!string.IsNullOrEmpty(path3) && File.Exists(path3) && item.ThirdPartyRating2.HasValue)
            validRatings.Add((path3, item.ThirdPartyRating2.Value));

        return validRatings;
    }

    private Font? TryLoadCompositeFont(int boxHeight, int innerWidth)
    {
        try
        {
            var maxFontByHeight = boxHeight * CompositeFontHeightPct;
            var maxFontByWidth = innerWidth * CompositeFontWidthPct;
            var fontSize = Math.Max(10f, Math.Min(maxFontByHeight, maxFontByWidth));

            if (SystemFonts.TryGet("Arial", out var fontFamily))
            {
                return fontFamily.CreateFont(fontSize, FontStyle.Bold);
            }

            if (SystemFonts.Families.Any())
            {
                return SystemFonts.Families.First().CreateFont(fontSize, FontStyle.Bold);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load font for composite ratings overlay.");
        }

        return null;
    }

    private async Task DrawCompositeRatingRowAsync(
        Image<Rgba32> baseImage,
        (string ImagePath, decimal Score) rating,
        int targetX,
        int targetY,
        int containerWidth,
        int boxHeight,
        int gap,
        float radius,
        int innerWidth,
        int index,
        Font? font,
        CancellationToken cancellationToken = default)
    {
        var currentBoxY = targetY + (index * (boxHeight + gap));

        baseImage.Mutate(x => DrawRoundedBackground(x, targetX, currentBoxY, containerWidth, boxHeight, radius));

        using var logoImage = await Image.LoadAsync<Rgba32>(rating.ImagePath, cancellationToken);

        var maxLogoW = Math.Max(1, innerWidth);
        var maxLogoH = Math.Max(1, (int)(boxHeight * CompositeLogoHeightPct));

        logoImage.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(maxLogoW, maxLogoH),
            Mode = ResizeMode.Max,
            Sampler = KnownResamplers.Lanczos3
        }));

        var logoX = targetX + ((containerWidth - logoImage.Width) / 2);
        var logoY = currentBoxY + (int)(boxHeight * CompositeLogoTopPct);

        baseImage.Mutate(x => x.DrawImage(logoImage, new Point(logoX, logoY), 1f));

        if (font == null)
        {
            return;
        }

        string scoreText;
        if (BadgeResolver.IsRottenTomatoesLogo(rating.ImagePath))
        {
            scoreText = $"{Math.Round(rating.Score)}%";
        }
        else if (BadgeResolver.IsVoraStarLogo(rating.ImagePath))
        {
            scoreText = rating.Score.ToString("0");
        }
        else
        {
            scoreText = rating.Score.ToString("0.0");
        }

        var textOptions = new RichTextOptions(font)
        {
            Origin = new PointF(targetX + (containerWidth / 2f), currentBoxY + (boxHeight * CompositeTextYPct)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        baseImage.Mutate(x => x.DrawText(textOptions, scoreText, Color.White));
    }
}
