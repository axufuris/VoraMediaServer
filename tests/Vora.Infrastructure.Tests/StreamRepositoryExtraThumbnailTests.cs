using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class StreamRepositoryExtraThumbnailTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("extra-thumbnail-source-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static MediaExtra Extra(Movie movie, params MediaPart[] parts)
    {
        var extra = new MediaExtra { Id = Guid.NewGuid(), Title = "Teaser", ExtraType = MediaExtraType.Trailer, MediaItemId = movie.Id };
        foreach (var part in parts)
        {
            part.MediaExtraId = extra.Id;
            extra.Parts.Add(part);
        }
        return extra;
    }

    [Fact]
    public async Task An_extra_is_found_with_its_first_part_and_its_titles_backdrop()
    {
        using var db = NewContext();
        var movie = new Movie { Id = Guid.NewGuid(), Title = "The Movie", BackgroundUrl = "https://image.example/backdrop.jpg" };
        var second = new MediaPart { Id = Guid.NewGuid(), FilePath = "/media/Teaser.cd2.mkv", PartNumber = 2 };
        var first = new MediaPart { Id = Guid.NewGuid(), FilePath = "/media/Teaser.cd1.mkv", PartNumber = 1, Duration = TimeSpan.FromSeconds(95) };
        var extra = Extra(movie, second, first);
        db.Add(movie);
        db.Add(extra);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var source = await new StreamRepository(db).GetExtraThumbnailSourceAsync(extra.Id);

        Assert.NotNull(source);
        Assert.Equal(extra.Id, source.ExtraId);
        Assert.Equal("https://image.example/backdrop.jpg", source.FallbackUrl);
        Assert.NotNull(source.Part);
        Assert.Equal(first.Id, source.Part.Id);
        Assert.Equal("/media/Teaser.cd1.mkv", source.Part.FilePath);
        Assert.Equal(TimeSpan.FromSeconds(95), source.Part.Duration);
    }

    [Fact]
    public async Task An_extra_with_no_file_or_backdrop_is_still_found()
    {
        using var db = NewContext();
        var movie = new Movie { Id = Guid.NewGuid(), Title = "The Movie" };
        var extra = Extra(movie);
        db.Add(movie);
        db.Add(extra);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var source = await new StreamRepository(db).GetExtraThumbnailSourceAsync(extra.Id);

        Assert.NotNull(source);
        Assert.Null(source.Part);
        Assert.Null(source.FallbackUrl);
    }

    [Fact]
    public async Task An_unknown_extra_is_not_found()
    {
        using var db = NewContext();

        Assert.Null(await new StreamRepository(db).GetExtraThumbnailSourceAsync(Guid.NewGuid()));
    }
}
