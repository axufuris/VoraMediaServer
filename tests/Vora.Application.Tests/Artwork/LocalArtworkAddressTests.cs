using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Artwork;
using Vora.Application.Media.Dtos;
using Vora.Application.Metadata.Local;
using Vora.Plugins.Providers.Local;

namespace Vora.Application.Tests.Artwork;

public class LocalArtworkAddressTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vora-local-art-" + Guid.NewGuid().ToString("N"));

    public LocalArtworkAddressTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Write(string name)
    {
        var path = Path.GetFullPath(Path.Combine(_directory, name));
        var parent = Path.GetDirectoryName(path);
        if (parent != null) Directory.CreateDirectory(parent);
        File.WriteAllText(path, "");
        return path;
    }

    private LocalMediaPlace Place(string kind, IReadOnlyList<string> paths, int? season = null)
    {
        var folders = new LocalMediaFolders(NullLogger.Instance);
        var place = LocalMediaPlace.From(new LocalMediaSourceDto(Guid.NewGuid(), kind, season, null, paths), folders);
        return Assert.IsType<LocalMediaPlace>(place);
    }

    private static string? Resolve(LocalMediaPlace place, string fileName) =>
        place.Resolve(fileName, new LocalMediaFolders(NullLogger.Instance));

    [Theory]
    [InlineData("poster.jpg")]
    [InlineData("Once Upon a Time... in Hollywood (2019)-poster.jpg")]
    [InlineData("Amélie #1 & friends-fanart.png")]
    public void An_address_names_the_item_and_the_file_and_reads_back_the_same(string fileName)
    {
        var id = Guid.NewGuid();
        var path = Write(fileName);

        var address = LocalArtworkAddress.Build(id, path);

        address.Should().StartWith($"/api/artwork/local/{id}/").And.Contain("?v=").And.NotContain(_directory);
        LocalArtworkAddress.TryParse(address, out var parsedId, out var parsedName).Should().BeTrue();
        parsedId.Should().Be(id);
        parsedName.Should().Be(fileName);
    }

    [Fact]
    public void Replacing_the_file_changes_the_address_so_cached_copies_are_not_reused()
    {
        var id = Guid.NewGuid();
        var path = Write("poster.jpg");
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var before = LocalArtworkAddress.Build(id, path);

        File.SetLastWriteTimeUtc(path, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

        LocalArtworkAddress.Build(id, path).Should().NotBe(before);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/api/artwork/custom/poster.jpg")]
    [InlineData("https://image.tmdb.org/t/p/w500/poster.jpg")]
    [InlineData("/api/artwork/local/not-a-guid/poster.jpg")]
    [InlineData("/api/artwork/local/7f1c1a52-6d0f-4d0b-9c58-0c6a6f0b4c11/")]
    public void Anything_else_is_not_a_local_address(string? address)
    {
        LocalArtworkAddress.TryParse(address, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void A_film_serves_its_own_images_and_the_folders_shared_ones()
    {
        var video = Write("Alien (1979)/Alien (1979).mkv");
        var poster = Write("Alien (1979)/poster.jpg");
        var own = Write("Alien (1979)/Alien (1979)-fanart.png");
        var place = Place(LocalMediaSourceDto.Movie, [video]);

        Resolve(place, "poster.jpg").Should().Be(poster);
        Resolve(place, "POSTER.JPG").Should().Be(poster);
        Resolve(place, "Alien (1979)-fanart.png").Should().Be(own);
    }

    [Theory]
    [InlineData("movie.nfo")]
    [InlineData("Alien (1979).mkv")]
    [InlineData("holiday.jpg")]
    [InlineData("Heat (1995)-poster.jpg")]
    [InlineData("../Heat (1995)/poster.jpg")]
    [InlineData("..\\Heat (1995)\\poster.jpg")]
    [InlineData("missing-poster.jpg")]
    public void Nothing_but_this_titles_own_artwork_can_be_asked_for(string fileName)
    {
        var video = Write("Alien (1979)/Alien (1979).mkv");
        Write("Alien (1979)/movie.nfo");
        Write("Alien (1979)/holiday.jpg");
        Write("Alien (1979)/Heat (1995)-poster.jpg");
        Write("Heat (1995)/poster.jpg");
        var place = Place(LocalMediaSourceDto.Movie, [video]);

        Resolve(place, fileName).Should().BeNull();
    }

    [Fact]
    public void A_season_serves_images_from_its_own_folder_and_numbered_ones_from_the_show_folder()
    {
        var episode = Write("Show/Season 02/Show - S02E01 - Second.mkv");
        var own = Write("Show/Season 02/poster.jpg");
        var numbered = Write("Show/season02-poster.jpg");
        Write("Show/season03-poster.jpg");
        var place = Place(LocalMediaSourceDto.Season, [episode], season: 2);

        Resolve(place, "poster.jpg").Should().Be(own);
        Resolve(place, "season02-poster.jpg").Should().Be(numbered);
        Resolve(place, "season03-poster.jpg").Should().BeNull();
    }

    [Fact]
    public void A_show_spread_over_two_folders_serves_images_from_either()
    {
        var first = Write("HD/Show/Season 01/Show - S01E01 - First.mkv");
        var second = Write("4K/Show/Season 01/Show - S01E01 - First.mkv");
        var logo = Write("4K/Show/logo.png");
        var place = Place(LocalMediaSourceDto.TvShow, [first, second]);

        place.Directories.Should().HaveCount(2);
        Resolve(place, "logo.png").Should().Be(logo);
    }
}
