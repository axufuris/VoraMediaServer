using Vora.Application.Backups;
using Vora.Application.Media;

namespace Vora.Application.Tests.Backups;

public class BackupIdentityKeysTests
{
    [Fact]
    public void A_movie_leads_with_the_media_trash_content_key_then_each_provider_then_its_title()
    {
        var movie = new ContentIdentitySource
        {
            Type = "movie",
            Title = "The Matrix",
            ReleaseDate = new DateOnly(1999, 3, 31),
            TmdbId = "603",
            ImdbId = "tt0133093"
        };

        var keys = BackupIdentityKeys.ForVideo(movie);

        keys.Should().Equal("movie:tmdb:603", "movie:imdb:tt0133093", "movie:title:the matrix|1999");
        keys[0].Should().Be(ContentIdentity.Compute(movie));
    }

    [Fact]
    public void An_episode_is_keyed_by_its_series_and_numbers()
    {
        var episode = new ContentIdentitySource
        {
            Type = "episode",
            Title = "Pilot",
            SeasonNumber = 1,
            EpisodeNumber = 2,
            SeriesTitle = "Breaking  Bad",
            SeriesReleaseDate = new DateOnly(2008, 1, 20),
            SeriesTvdbId = "81189"
        };

        BackupIdentityKeys.ForVideo(episode).Should().Equal("episode:tvdb:81189:1:2", "episode:title:breaking bad|2008:1:2");
    }

    [Fact]
    public void An_unmatched_video_still_gets_a_title_key()
    {
        var homeVideo = new ContentIdentitySource { Type = "movie", Title = "Birthday 2019" };

        BackupIdentityKeys.ForVideo(homeVideo).Should().Equal("movie:title:birthday 2019|");
    }

    [Fact]
    public void A_track_prefers_the_album_musicbrainz_id_and_falls_back_to_normalized_names()
    {
        var track = new MusicTrackIdentitySource
        {
            Title = "Paranoid  Android",
            TrackNumber = 2,
            AlbumTitle = "OK Computer",
            AlbumMusicBrainzId = "B1392450-E666-3926-A536-22C65F834433",
            ArtistName = "Radiohead"
        };

        BackupIdentityKeys.ForTrack(track).Should().Equal(
            "track:mbid:b1392450-e666-3926-a536-22c65f834433:1:2:paranoid android",
            "track:name:radiohead|ok computer|1|2|paranoid android");
    }

    [Fact]
    public void Curly_quotes_and_case_do_not_change_a_music_key()
    {
        var typed = new MusicAlbumIdentitySource { Title = "Don't Stop", ArtistName = "Fleetwood Mac" };
        var tagged = new MusicAlbumIdentitySource { Title = "DON’T STOP", ArtistName = "fleetwood mac" };

        BackupIdentityKeys.ForAlbum(typed).Should().Equal(BackupIdentityKeys.ForAlbum(tagged));
    }

    [Fact]
    public void Artists_libraries_collections_and_channels_have_stable_keys()
    {
        BackupIdentityKeys.ForArtist(new MusicArtistIdentitySource { Name = "Björk", MusicBrainzId = "87C5DEDD-371D-4A53-9F7F-80522FB7F3CB" })
            .Should().Equal("artist:mbid:87c5dedd-371d-4a53-9f7f-80522fb7f3cb", "artist:name:björk");
        BackupIdentityKeys.ForLibrary("Movie", " Movies ").Should().Equal("library:movie:movies");
        BackupIdentityKeys.ForCollection(86311, null, null, "The Avengers Collection")
            .Should().Equal("collection:tmdb:86311", "collection:title:the avengers collection");
        BackupIdentityKeys.ForChannel(Guid.Parse("11111111-1111-1111-1111-111111111111"), "bbc1.uk")
            .Should().Equal("channel:11111111111111111111111111111111:bbc1.uk");
    }
}
