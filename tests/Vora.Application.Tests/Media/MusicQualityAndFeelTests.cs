using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Media;

public class MusicQualityAndFeelTests
{
    private readonly IMusicRepository _repository = Substitute.For<IMusicRepository>();
    private readonly MusicManager _manager;

    public MusicQualityAndFeelTests()
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
    }

    [Theory]
    [InlineData("flac", 96000, 2900, "Hi-Res · FLAC 96 kHz", true, true)]
    [InlineData("flac", 192000, 5000, "Hi-Res · FLAC 192 kHz", true, true)]
    [InlineData("flac", 88200, 2000, "Hi-Res · FLAC 88.2 kHz", true, true)]
    [InlineData("FLAC", 44100, 1, "Lossless · FLAC 44.1 kHz", true, false)]
    [InlineData("alac", 48000, 900, "Lossless · ALAC 48 kHz", true, false)]
    [InlineData("pcm_s24le", 96000, 4608, "Hi-Res · PCM 96 kHz", true, true)]
    [InlineData("mp3", 44100, 320, "MP3 · 320 kbps", false, false)]
    [InlineData("aac", 44100, 0, "AAC", false, false)]
    [InlineData("opus", 48000, 160, "Opus · 160 kbps", false, false)]
    public void A_songs_quality_is_worded_once_for_every_client(string codec, int sampleRate, int bitrate, string label, bool lossless, bool hiRes)
    {
        var quality = AudioQuality.For(codec, sampleRate, bitrate);

        quality!.Label.Should().Be(label);
        quality.Lossless.Should().Be(lossless);
        quality.HiRes.Should().Be(hiRes);
    }

    [Fact]
    public void A_song_with_no_codec_has_no_quality()
    {
        AudioQuality.For(null, 44100, 320).Should().BeNull();
        AudioQuality.For("  ", 44100, 320).Should().BeNull();
    }

    [Fact]
    public void An_album_shows_its_most_common_format_at_its_best_sample_rate()
    {
        var tracks = new[]
        {
            new Track { Title = "1", AudioCodec = "flac", SampleRate = 44100 },
            new Track { Title = "2", AudioCodec = "flac", SampleRate = 96000 },
            new Track { Title = "3", AudioCodec = "mp3", SampleRate = 44100, Bitrate = 320 },
        };

        AudioQuality.ForAlbum(tracks)!.Label.Should().Be("Hi-Res · FLAC 96 kHz");
    }

    [Fact]
    public void A_lossy_album_shows_its_most_common_bitrate()
    {
        var tracks = new[]
        {
            new Track { Title = "1", AudioCodec = "mp3", Bitrate = 320 },
            new Track { Title = "2", AudioCodec = "mp3", Bitrate = 320 },
            new Track { Title = "3", AudioCodec = "mp3", Bitrate = 192 },
        };

        AudioQuality.ForAlbum(tracks)!.Label.Should().Be("MP3 · 320 kbps");
        AudioQuality.ForAlbum(Array.Empty<Track>()).Should().BeNull();
    }

    [Fact]
    public void An_albums_moods_are_its_tracks_most_common_ones()
    {
        var tracks = new[]
        {
            new Track { Title = "1", Moods = new List<string> { "Euphoric", "upbeat" } },
            new Track { Title = "2", Moods = new List<string> { "upbeat", "nostalgic" } },
            new Track { Title = "3", Moods = new List<string> { "upbeat", "euphoric", "dreamy", "playful" } },
            new Track { Title = "4" },
        };

        MusicManager.CommonMoods(tracks).Should().Equal("upbeat", "euphoric", "nostalgic", "dreamy");
    }

    [Fact]
    public async Task A_songs_info_carries_its_quality_and_how_it_feels()
    {
        var track = new Track
        {
            Id = Guid.NewGuid(),
            Title = "One More Time",
            AudioCodec = "flac",
            SampleRate = 44100,
            Moods = new List<string> { "euphoric" },
            Energy = TrackEnergy.High,
            GoodFor = new List<string> { "party" },
            IsInstrumental = false
        };
        _repository.GetTrackByIdAsync(track.Id, Arg.Any<MusicAccessFilter>()).Returns(track);

        var info = await _manager.GetTrackInfoAsync(track.Id, MusicAccessFilter.Unrestricted);

        info!.Quality!.Label.Should().Be("Lossless · FLAC 44.1 kHz");
        info.Moods.Should().Equal("euphoric");
        info.Energy.Should().Be(TrackEnergy.High);
        info.GoodFor.Should().Equal("party");
        info.Themes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_song_the_profile_cannot_see_has_no_info()
    {
        _repository.GetTrackByIdAsync(Arg.Any<Guid>(), Arg.Any<MusicAccessFilter>()).Returns((Track?)null);

        (await _manager.GetTrackInfoAsync(Guid.NewGuid(), MusicAccessFilter.Unrestricted)).Should().BeNull();
    }
}
