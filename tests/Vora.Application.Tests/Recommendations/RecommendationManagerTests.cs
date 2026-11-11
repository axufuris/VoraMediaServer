using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.Recommendations;
using Vora.Application.Recommendations.ViewModels;
using Vora.Application.Settings;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Recommendations;

// For You loaded its tiles a row at a time: fifteen queries for one page.
public class RecommendationManagerTests
{
    private readonly IRecommendationRepository _repository = Substitute.For<IRecommendationRepository>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly IRecommendationProvider _provider = Substitute.For<IRecommendationProvider>();
    private readonly Guid _profile = Guid.NewGuid();
    private readonly List<List<Guid>> _asked = new();

    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid(), Matched = Guid.NewGuid();

    public RecommendationManagerTests()
    {
        _provider.Id.Returns("local");
        _repository.GetHydratedMediaItemsAsync(_profile, Arg.Any<List<Guid>>(), Arg.Any<List<string>>(), null, true, Arg.Any<List<Guid>>(), Arg.Any<List<string>>(), Arg.Any<List<string>>(), false)
            .Returns(call =>
            {
                var ids = call.ArgAt<List<Guid>>(1);
                var tmdb = call.ArgAt<List<string>>(2);
                _asked.Add(ids.ToList());
                // Returned in no particular order, as the database does.
                var found = ids.Where(id => id != D).Select(id => new LibraryItemVM { Id = id, Title = id.ToString() }).Reverse().ToList();
                if (tmdb.Contains("tt-1")) found.Add(new LibraryItemVM { Id = Matched, Title = "matched" });
                return found;
            });
    }

    private RecommendationManager Manager() =>
        new(new[] { _provider }, _settings, _repository, NullLogger<RecommendationManager>.Instance);

    private Task<List<RecommendationListVM>> RowsAsync() =>
        Manager().GetRecommendationsAsync(_profile, null, true, new(), new(), new(), false);

    [Fact]
    public async Task Every_row_of_library_titles_is_loaded_in_one_query_and_keeps_its_order()
    {
        _provider.GetRecommendationsAsync(_profile, null, Arg.Any<CancellationToken>()).Returns(new List<RecommendationListDto>
        {
            new() { Title = "Because you watched Heat", Weight = 1, LocalItemIds = new() { A, B, D } },
            new() { Title = "Because you like Crime", Weight = 2, LocalItemIds = new() { C, A } },
        });

        var rows = await RowsAsync();

        _asked.Should().ContainSingle().Which.Should().BeEquivalentTo(new[] { A, B, D, C });
        rows.Select(r => r.Title).Should().Equal("Because you watched Heat", "Because you like Crime");
        rows[0].Items.Select(i => i.Id).Should().Equal(A, B);
        rows[1].Items.Select(i => i.Id).Should().Equal(C, A);
    }

    [Fact]
    public async Task A_row_named_by_tmdb_ids_is_matched_on_its_own()
    {
        _provider.GetRecommendationsAsync(_profile, null, Arg.Any<CancellationToken>()).Returns(new List<RecommendationListDto>
        {
            new() { Title = "Local", Weight = 1, LocalItemIds = new() { A } },
            new() { Title = "From the AI", Weight = 2, ExternalTmdbIds = new() { "tt-1" } },
        });

        var rows = await RowsAsync();

        _asked.Should().HaveCount(2);
        rows.Select(r => r.Title).Should().Equal("Local", "From the AI");
        rows[1].Items.Select(i => i.Id).Should().Equal(Matched);
    }

    [Fact]
    public async Task A_row_none_of_whose_titles_can_be_shown_is_left_out()
    {
        _provider.GetRecommendationsAsync(_profile, null, Arg.Any<CancellationToken>()).Returns(new List<RecommendationListDto>
        {
            new() { Title = "Gone", Weight = 1, LocalItemIds = new() { D } },
            new() { Title = "Here", Weight = 2, LocalItemIds = new() { B } },
        });

        (await RowsAsync()).Select(r => r.Title).Should().Equal("Here");
    }
}
