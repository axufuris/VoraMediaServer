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

    private static readonly OverlayElementDto Ratings = new() { Type = "composite_ratings", XPct = 0.6, YPct = 0.4, WidthPct = 0.3, HeightPct = 0.45 };

    private static int SlotTop(int slot)
    {
        var containerHeight = (int)(Height * Ratings.HeightPct);
        var gap = (int)Math.Max(2, containerHeight * 0.04f);
        var boxHeight = (containerHeight - (gap * 2)) / 3;
        return (int)(Height * Ratings.YPct) + (slot * (boxHeight + gap));
    }

    private static bool SlotIsDrawn(Image<Rgba32> poster, int slot) =>
        poster[(int)(Width * Ratings.XPct) + 4, SlotTop(slot) + 40].R < 120;

    [Fact]
    public async Task A_missing_server_rating_leaves_its_slot_empty_and_the_library_ratings_where_they_always_are()
    {
        var item = new OverlayMediaDto
        {
            Id = Guid.NewGuid(),
            MediaType = "Movie",
            ThirdPartyRating1Name = "TMDb",
            ThirdPartyRating1 = 6.5m,
            ThirdPartyRating2Name = "Rotten Tomatoes",
            ThirdPartyRating2 = 53m,
        };

        using var poster = await RenderAsync(item, Ratings);

        SlotIsDrawn(poster, 0).Should().BeFalse();
        SlotIsDrawn(poster, 1).Should().BeTrue();
        SlotIsDrawn(poster, 2).Should().BeTrue();
    }

    [Fact]
    public async Task Each_rating_keeps_its_own_slot_whichever_ones_a_title_has()
    {
        var item = new OverlayMediaDto
        {
            Id = Guid.NewGuid(),
            MediaType = "Movie",
            ServerAdminRating = 8m,
            ThirdPartyRating2Name = "Rotten Tomatoes",
            ThirdPartyRating2 = 86m,
        };

        using var poster = await RenderAsync(item, Ratings);

        SlotIsDrawn(poster, 0).Should().BeTrue();
        SlotIsDrawn(poster, 1).Should().BeFalse();
        SlotIsDrawn(poster, 2).Should().BeTrue();
    }

    [Fact]
    public async Task A_title_with_every_rating_fills_the_three_slots()
    {
        var item = new OverlayMediaDto
        {
            Id = Guid.NewGuid(),
            MediaType = "Movie",
            ServerAdminRating = 5m,
            ThirdPartyRating1Name = "TMDb",
            ThirdPartyRating1 = 7.1m,
            ThirdPartyRating2Name = "Rotten Tomatoes",
            ThirdPartyRating2 = 86m,
        };

        using var poster = await RenderAsync(item, Ratings);

        Enumerable.Range(0, 3).Select(slot => SlotIsDrawn(poster, slot)).Should().AllBeEquivalentTo(true);
    }
}
