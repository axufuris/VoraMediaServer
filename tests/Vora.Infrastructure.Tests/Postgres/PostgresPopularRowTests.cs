using Microsoft.EntityFrameworkCore;
using Vora.Application.SmartLists.Dtos;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Streaming;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresPopularRowTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    [Fact]
    public async Task The_popular_sort_orders_by_viewers_then_rating_on_a_real_database()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var tag = Guid.NewGuid().ToString("N")[..8];
        var library = new MediaLibrary { Name = "Popular " + tag, Type = LibraryType.TvShow, FolderPaths = new List<string> { "/popular" } };
        var account = new User { Email = $"{tag}@example.com", DisplayName = "Andy" };
        var ann = new UserProfile { Name = "Ann", UserId = account.Id };
        var ben = new UserProfile { Name = "Ben", UserId = account.Id };
        var cy = new UserProfile { Name = "Cy", UserId = account.Id };
        var device = new ClientDevice { DeviceId = "web-" + tag, ClientName = "Vora Web", DeviceName = "Web Browser" };
        var show = new TvShow { Title = "Show " + tag, LibraryId = library.Id, ThirdPartyRating1 = 7.0m };
        var season = new Season { Title = "Season 1", SeasonNumber = 1, TvShowId = show.Id, LibraryId = library.Id };
        var first = new Episode { Title = "E1", EpisodeNumber = 1, SeasonId = season.Id, LibraryId = library.Id };
        var second = new Episode { Title = "E2", EpisodeNumber = 2, SeasonId = season.Id, LibraryId = library.Id };
        var twice = new Movie { Title = "Two viewers " + tag, LibraryId = library.Id, ThirdPartyRating1 = 5.0m };
        var once = new Movie { Title = "One viewer " + tag, LibraryId = library.Id, ThirdPartyRating1 = 9.0m };
        var loved = new Movie { Title = "Unplayed loved " + tag, LibraryId = library.Id, ThirdPartyRating1 = 8.0m };
        var unrated = new Movie { Title = "Unplayed unrated " + tag, LibraryId = library.Id };

        StreamSession Play(MediaItem item, UserProfile profile, Guid? extraId = null) => new()
        {
            MediaItemId = item.Id,
            MediaPartId = Guid.NewGuid(),
            UserId = account.Id,
            UserProfileId = profile.Id,
            ClientDeviceId = device.Id,
            ExtraId = extraId,
        };

        await using (var seed = database.NewContext())
        {
            seed.AddRange(library, account, ann, ben, cy, device, show, season, first, second, twice, once, loved, unrated);
            seed.AddRange(
                Play(first, ann), Play(first, ann), Play(second, ben), Play(second, cy),
                Play(twice, ann), Play(twice, ben),
                Play(once, ann), Play(once, ann), Play(once, ann),
                Play(loved, ann, Guid.NewGuid()));
            await seed.SaveChangesAsync(cancellationToken);
        }

        await using var db = database.NewContext();
        var items = await new SmartListRepository(db).GetSmartListItemsAsync(
            Guid.NewGuid(), library.Id, new SmartListRulesDto { MediaTypes = ["Movie", "TvShow"] }, SmartListSortBy.Popular, 10);

        items.Select(i => i.Title).Should().Equal(show.Title, twice.Title, once.Title, loved.Title, unrated.Title);
    }
}
