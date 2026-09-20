using Vora.Plugins.Dtos;

namespace Vora.Application.Tests.Plugins;

public class LibraryKindExtensionsTests
{
    [Theory]
    [InlineData("Movie", LibraryKind.Movie)]
    [InlineData("TvShow", LibraryKind.TvShow)]
    [InlineData("Music", LibraryKind.Music)]
    [InlineData("HomeVideo", LibraryKind.HomeVideo)]
    [InlineData("Photo", LibraryKind.Photo)]
    [InlineData("Podcast", LibraryKind.Podcast)]
    [InlineData("Audiobook", LibraryKind.Audiobook)]
    public void TryParseLibraryKind_recognises_each_enum_value(string input, LibraryKind expected)
    {
        var ok = LibraryKindExtensions.TryParseLibraryKind(input, out var kind);

        ok.Should().BeTrue();
        kind.Should().Be(expected);
    }

    [Theory]
    [InlineData("movie", LibraryKind.Movie)]
    [InlineData("MOVIE", LibraryKind.Movie)]
    [InlineData("tvshow", LibraryKind.TvShow)]
    [InlineData("TVSHOW", LibraryKind.TvShow)]
    public void TryParseLibraryKind_is_case_insensitive(string input, LibraryKind expected)
    {
        var ok = LibraryKindExtensions.TryParseLibraryKind(input, out var kind);

        ok.Should().BeTrue();
        kind.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void TryParseLibraryKind_rejects_empty_or_whitespace(string input)
    {
        var ok = LibraryKindExtensions.TryParseLibraryKind(input, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void TryParseLibraryKind_rejects_null()
    {
        var ok = LibraryKindExtensions.TryParseLibraryKind(null!, out _);

        ok.Should().BeFalse();
    }

    [Theory]
    [InlineData("NotARealKind")]
    [InlineData("Book")]
    [InlineData("ShortFilm")]
    public void TryParseLibraryKind_rejects_unknown_values(string input)
    {
        var ok = LibraryKindExtensions.TryParseLibraryKind(input, out _);

        ok.Should().BeFalse();
    }
}
