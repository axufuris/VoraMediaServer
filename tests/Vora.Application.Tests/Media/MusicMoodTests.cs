using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Media;

public class MusicMoodTests
{
    private readonly IMusicRepository _repository = Substitute.For<IMusicRepository>();
    private readonly MusicManager _manager;

    public MusicMoodTests()
    {
        _manager = new MusicManager(
            _repository,
            Substitute.For<IUserRepository>(),
            Substitute.For<IUserMediaStateRepository>(),
            Array.Empty<IMusicArtworkProvider>(),
            Array.Empty<ILyricsProvider>(),
            Array.Empty<IListeningDataProvider>(),
            Substitute.For<IClientNotifier>(),
            Options.Create(new StoragePathsOptions()),
            new NullTaskProgressReporter(),
            NullLogger<MusicManager>.Instance);
        _repository.GetTracksForMoodAsync(Arg.Any<string>(), Arg.Any<MusicAccessFilter>(), Arg.Any<int>(), Arg.Any<int>()).Returns(new List<Track>());
    }

    private static Track Song(string title, string? cover) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Album = new Album { Title = title + " album", ArtworkUrl = cover }
    };

    [Theory]
    [InlineData("Chill", "chill")]
    [InlineData(" mellow ", "chill")]
    [InlineData("melancholic", "melancholy")]
    [InlineData("triumphant", "epic")]
    [InlineData("warm", null)]
    [InlineData("", null)]
    public void A_word_becomes_one_of_the_fixed_moods_or_nothing(string word, string? mood)
    {
        SongMoods.Normalize(word).Should().Be(mood);
    }

    [Fact]
    public void A_songs_moods_keep_their_order_without_repeats()
    {
        SongMoods.Normalize(new[] { "Wistful", "nostalgic", "warm", "relaxed" }).Should().Equal("nostalgic", "chill");
    }

    [Fact]
    public async Task Moods_come_in_list_order_and_only_with_enough_songs()
    {
        _repository.GetMoodTrackCountsAsync(Arg.Any<MusicAccessFilter>()).Returns(new Dictionary<string, int>
        {
            ["sad"] = 40,
            ["happy"] = 12,
            ["epic"] = SongMoods.MinTracksToBrowse - 1,
            ["warm"] = 300
        });

        var moods = await _manager.GetMoodsAsync(MusicAccessFilter.Unrestricted);

        moods.Select(m => m.Mood).Should().Equal("happy", "sad");
        moods.Select(m => m.Name).Should().Equal("Happy", "Sad");
        moods.Select(m => m.TrackCount).Should().Equal(12, 40);
    }

    [Fact]
    public async Task Each_mood_tile_gets_a_cover_the_others_have_not_used()
    {
        _repository.GetMoodTrackCountsAsync(Arg.Any<MusicAccessFilter>()).Returns(new Dictionary<string, int> { ["happy"] = 10, ["upbeat"] = 10 });
        _repository.GetTracksForMoodAsync("happy", Arg.Any<MusicAccessFilter>(), 0, MusicManager.MoodArtworkCandidates)
            .Returns(new List<Track> { Song("Hit", "/hit.jpg") });
        _repository.GetTracksForMoodAsync("upbeat", Arg.Any<MusicAccessFilter>(), 0, MusicManager.MoodArtworkCandidates)
            .Returns(new List<Track> { Song("Hit", "/hit.jpg"), Song("No cover", null), Song("Other", "/other.jpg") });

        var moods = await _manager.GetMoodsAsync(MusicAccessFilter.Unrestricted);

        moods.Select(m => m.SampleArtworkUrl).Should().Equal("/hit.jpg", "/other.jpg");
    }

    [Fact]
    public async Task A_moods_page_reads_the_mood_by_any_of_its_names_and_caps_the_page()
    {
        _repository.CountTracksForMoodAsync("chill", Arg.Any<MusicAccessFilter>()).Returns(450);
        _repository.GetTracksForMoodAsync("chill", Arg.Any<MusicAccessFilter>(), 100, MusicManager.MaxMoodTracks)
            .Returns(new List<Track> { Song("Teardrop", "/mezzanine.jpg") });

        var page = await _manager.GetMoodTracksAsync("Laid-back", null, MusicAccessFilter.Unrestricted, 100, 5000);

        page.Should().NotBeNull();
        page?.Mood.Should().Be("chill");
        page?.Name.Should().Be("Chill");
        page?.TotalCount.Should().Be(450);
        page?.Tracks.Select(t => t.AlbumArtworkUrl).Should().Equal("/mezzanine.jpg");
    }

    [Fact]
    public async Task An_unknown_mood_has_no_page_and_no_shuffle()
    {
        (await _manager.GetMoodTracksAsync("warm", null, MusicAccessFilter.Unrestricted, 0, 50)).Should().BeNull();
        (await _manager.GetMoodShuffleAsync("warm", null, MusicAccessFilter.Unrestricted, 50)).Should().BeNull();
        await _repository.DidNotReceive().GetRandomTracksForMoodAsync(Arg.Any<string>(), Arg.Any<MusicAccessFilter>(), Arg.Any<int>());
    }

    [Fact]
    public async Task A_mood_shuffle_asks_for_a_capped_number_of_songs()
    {
        _repository.GetRandomTracksForMoodAsync("groovy", Arg.Any<MusicAccessFilter>(), MusicManager.MaxMoodTracks)
            .Returns(new List<Track> { Song("Get Lucky", "/ram.jpg") });

        var shuffled = await _manager.GetMoodShuffleAsync("funky", null, MusicAccessFilter.Unrestricted, 999);

        shuffled.Should().NotBeNull();
        shuffled?.Select(t => t.Title).Should().Equal("Get Lucky");
    }
}
