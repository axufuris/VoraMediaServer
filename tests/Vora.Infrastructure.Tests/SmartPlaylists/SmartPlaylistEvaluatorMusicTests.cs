using Vora.Application.Media;
using Vora.Application.Media.SmartPlaylists;
using Vora.Domain.Entities.Playlists;

namespace Vora.Infrastructure.Tests.SmartPlaylists;

public class SmartPlaylistEvaluatorMusicTests
{
    private readonly SmartPlaylistEvaluatorFixture _fx = new();

    private static SmartPlaylistDefinition Definition(SmartPlaylistRuleGroup root, SmartPlaylistSortBy sortBy = SmartPlaylistSortBy.Title) => new()
    {
        Root = root,
        SortBy = sortBy,
        SortDirection = SmartPlaylistSortDirection.Asc
    };

    private static SmartPlaylistRuleGroup AllOf(params SmartPlaylistRule[] rules) =>
        new() { Match = SmartPlaylistMatch.All, Rules = rules.ToList() };

    private static SmartPlaylistRule Rule(SmartPlaylistField field, SmartPlaylistOperator op, string? value = null, string? secondValue = null) =>
        new() { Field = field, Operator = op, Value = value, SecondValue = secondValue };

    [Fact]
    public async Task Empty_rule_group_returns_all_tracks()
    {
        var artist = _fx.AddArtist("Artist A");
        var album = _fx.AddAlbum(artist, "Album A", 2020);
        _fx.AddTrack(album, "Track 1", 1);
        _fx.AddTrack(album, "Track 2", 2);

        var def = Definition(AllOf());
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Music, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Select(t => t.Title).Should().BeEquivalentTo(new[] { "Track 1", "Track 2" });
    }

    [Fact]
    public async Task AlbumTitle_Equals_filters_to_specific_album()
    {
        var artist = _fx.AddArtist("Artist A");
        var albumA = _fx.AddAlbum(artist, "Album A");
        var albumB = _fx.AddAlbum(artist, "Album B");
        _fx.AddTrack(albumA, "From A", 1);
        _fx.AddTrack(albumB, "From B", 1);

        var def = Definition(AllOf(Rule(SmartPlaylistField.AlbumTitle, SmartPlaylistOperator.Equals, "Album A")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Music, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Select(t => t.Title).Should().BeEquivalentTo(new[] { "From A" });
    }

    [Fact]
    public async Task Year_GreaterThan_filters_by_album_year()
    {
        var artist = _fx.AddArtist("A");
        var oldAlbum = _fx.AddAlbum(artist, "Old", year: 1995);
        var newAlbum = _fx.AddAlbum(artist, "New", year: 2020);
        _fx.AddTrack(oldAlbum, "Old Track", 1);
        _fx.AddTrack(newAlbum, "New Track", 1);

        var def = Definition(AllOf(Rule(SmartPlaylistField.Year, SmartPlaylistOperator.GreaterThan, "2000")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Music, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Select(t => t.Title).Should().BeEquivalentTo(new[] { "New Track" });
    }

    [Fact]
    public async Task ContentRating_filters_explicit_content()
    {
        var artist = _fx.AddArtist("A");
        var album = _fx.AddAlbum(artist, "X");
        _fx.AddTrack(album, "Clean Track", 1, contentRating: "Clean");
        _fx.AddTrack(album, "Explicit Track", 2, contentRating: "Explicit");
        _fx.AddTrack(album, "Unrated Track", 3);

        var def = Definition(AllOf(Rule(SmartPlaylistField.ContentRating, SmartPlaylistOperator.Equals, "Clean")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Music, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Select(t => t.Title).Should().BeEquivalentTo(new[] { "Clean Track" });
    }

    [Fact]
    public async Task IsCompilation_filters_compilation_albums()
    {
        var artist = _fx.AddArtist("A");
        var normal = _fx.AddAlbum(artist, "Studio", isCompilation: false);
        var comp = _fx.AddAlbum(artist, "Greatest Hits", isCompilation: true);
        _fx.AddTrack(normal, "Studio Track", 1);
        _fx.AddTrack(comp, "Compilation Track", 1);

        var def = Definition(AllOf(Rule(SmartPlaylistField.IsCompilation, SmartPlaylistOperator.Equals, "true")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Music, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Select(t => t.Title).Should().BeEquivalentTo(new[] { "Compilation Track" });
    }

    [Fact]
    public async Task TrackNumber_LessThan_filters_within_album()
    {
        var artist = _fx.AddArtist("A");
        var album = _fx.AddAlbum(artist, "X");
        for (int i = 1; i <= 5; i++) _fx.AddTrack(album, $"Track {i}", i);

        var def = Definition(AllOf(Rule(SmartPlaylistField.TrackNumber, SmartPlaylistOperator.LessThan, "3")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Music, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Select(t => t.Title).Should().BeEquivalentTo(new[] { "Track 1", "Track 2" });
    }

    [Fact]
    public async Task DurationSeconds_Between_filters_by_length()
    {
        var artist = _fx.AddArtist("A");
        var album = _fx.AddAlbum(artist, "X");
        _fx.AddTrack(album, "Short", 1, durationSeconds: 60);
        _fx.AddTrack(album, "Medium", 2, durationSeconds: 200);
        _fx.AddTrack(album, "Long", 3, durationSeconds: 500);

        var def = Definition(AllOf(Rule(SmartPlaylistField.DurationSeconds, SmartPlaylistOperator.Between, "100", "300")));
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Music, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Select(t => t.Title).Should().BeEquivalentTo(new[] { "Medium" });
    }

    [Fact]
    public async Task Library_filter_in_access_restricts_tracks()
    {
        // Track is in _fx.LibraryId; access only allows a different library id.
        var artist = _fx.AddArtist("A");
        var album = _fx.AddAlbum(artist, "X");
        _fx.AddTrack(album, "Track", 1);

        var restricted = new MusicAccessFilter
        {
            HasAllLibraryAccess = false,
            AllowedLibraryIds = new List<Guid> { Guid.NewGuid() }  // Different from _fx.LibraryId
        };

        var def = Definition(AllOf());
        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Music, _fx.ProfileId, restricted);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task Limit_caps_music_results()
    {
        var artist = _fx.AddArtist("A");
        var album = _fx.AddAlbum(artist, "X");
        for (int i = 1; i <= 10; i++) _fx.AddTrack(album, $"Track {i:D2}", i);

        var def = new SmartPlaylistDefinition
        {
            Root = AllOf(),
            SortBy = SmartPlaylistSortBy.Title,
            SortDirection = SmartPlaylistSortDirection.Asc,
            Limit = 4
        };

        var results = await _fx.Evaluator.EvaluateAsync(def, PlaylistMediaType.Music, _fx.ProfileId, MusicAccessFilter.Unrestricted);

        results.Should().HaveCount(4);
    }
}
