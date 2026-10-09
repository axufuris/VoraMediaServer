using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Vora.Application.Posters.Dtos;
using Vora.Application.Posters.Providers;
using Vora.Plugins.Dtos;

namespace Vora.Application.Tests.Posters;

public class LocalImageSharpOverlayRenderingTests : IDisposable
{
    private const int Width = 600;
    private const int Height = 900;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-overlay-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private async Task<Image<Rgba32>> RenderAsync(OverlayMediaDto item, params OverlayElementDto[] elements)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "poster.png");
        using (var white = new Image<Rgba32>(Width, Height, new Rgba32(255, 255, 255, 255)))
        {
            await white.SaveAsPngAsync(source, cancellationToken);
        }

        var url = await new LocalImageSharpOverlayProvider(NullLogger<LocalImageSharpOverlayProvider>.Instance)
            .GenerateOverlayAsync(item, source, JsonSerializer.Serialize(elements), _root, cancellationToken);

        return await Image.LoadAsync<Rgba32>(Path.Combine(_root, url["/api/artwork/custom/".Length..]), cancellationToken);
    }

    [Fact]
    public async Task A_badge_sits_on_a_darkened_rounded_box()
    {
        var item = new OverlayMediaDto { Id = Guid.NewGuid(), MediaType = "Movie", Resolution = "2160p" };

        using var poster = await RenderAsync(item, new OverlayElementDto { Type = "resolution", XPct = 0.05, YPct = 0.05, WidthPct = 0.3, HeightPct = 0.1 });

        poster[32, 90].R.Should().BeLessThan(120);
        poster[Width / 2, Height / 2].R.Should().BeGreaterThan(230);
    }

    [Fact]
    public async Task Ratings_are_drawn_on_their_own_darkened_boxes_with_the_score_in_white()
    {
        var item = new OverlayMediaDto { Id = Guid.NewGuid(), MediaType = "Movie", ServerAdminRating = 8.4m };

        using var poster = await RenderAsync(item, new OverlayElementDto { Type = "composite_ratings", XPct = 0.6, YPct = 0.4, WidthPct = 0.3, HeightPct = 0.45 });

        var boxX = (int)(Width * 0.6);
        var boxY = (int)(Height * 0.4);
        poster[boxX + 4, boxY + 40].R.Should().BeLessThan(120);
        poster[Width / 4, Height / 4].R.Should().BeGreaterThan(230);

        if (SystemFonts.Families.Any())
        {
            var containerWidth = (int)(Width * 0.3);
            var containerHeight = (int)(Height * 0.45);
            var gap = (int)Math.Max(2, containerHeight * 0.04f);
            var boxHeight = (containerHeight - (gap * 2)) / 3;
            var textY = boxY + (int)(boxHeight * 0.75f);
            var brightest = 0;
            for (var x = boxX + 10; x < boxX + containerWidth - 10; x++)
            {
                for (var y = textY - 6; y <= textY + 6; y++)
                {
                    brightest = Math.Max(brightest, poster[x, y].R);
                }
            }

            brightest.Should().BeGreaterThan(200);
        }
    }
}
