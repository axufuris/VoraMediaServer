using Vora.Application.Metadata;
using Vora.Application.Metadata.Local;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Metadata;

public class ReleaseNameSearchTests
{
    private readonly IMetadataProvider _tmdb = Substitute.For<IMetadataProvider>();
    private readonly MetadataFetchService _service;

    public ReleaseNameSearchTests()
    {
        _tmdb.Id.Returns("tmdb_metadata");
        _service = new MetadataFetchService([_tmdb], [], [], Substitute.For<ILocalMediaReader>(), Substitute.For<IVideoFrameArtworkService>());
    }

    private static MediaLibrary Library(LibraryType type) => new() { Name = "Library", Type = type, MetadataProviderId = "tmdb_metadata" };

    [Fact]
    public async Task A_film_scanned_in_under_its_release_name_is_searched_by_its_title_and_year()
    {
        await _service.GetTextMetadataAsync(new Movie { Title = "Lee.2024.1080p", Library = Library(LibraryType.Movie) });

        await _tmdb.Received(1).FetchMovieMetadataAsync("Lee", 2024, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_show_scanned_in_under_its_release_name_is_searched_by_its_title_and_year()
    {
        await _service.GetTextMetadataAsync(new TvShow { Title = "Severance.2022.1080p.WEB-DL", Library = Library(LibraryType.TvShow) });

        await _tmdb.Received(1).FetchTvShowMetadataAsync("Severance", 2022, Arg.Any<CancellationToken>());
    }
}
