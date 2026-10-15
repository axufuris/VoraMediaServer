using Vora.Application.Media;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Providers.Local;

namespace Vora.Application.Tests.Scanner;

public class MusicIdentityTests
{
    private static string Music(params string[] parts) => Path.Combine([Path.GetTempPath(), "music", .. parts]);

    [Theory]
    [InlineData("CD1")]
    [InlineData("CD 2")]
    [InlineData("Disc 1")]
    [InlineData("disc_02")]
    [InlineData("Disk 3 - Bonus")]
    public void A_disc_folder_belongs_to_the_album_folder_above_it(string disc)
    {
        MusicAlbumFolders.AlbumFolder(Music("Artist", "Album", disc, "01.flac")).Should().Be(Music("Artist", "Album"));
    }

    [Theory]
    [InlineData("Discovery")]
    [InlineData("Weezer (Blue Album)")]
    public void Any_other_folder_is_the_album_folder(string folder)
    {
        MusicAlbumFolders.AlbumFolder(Music("Artist", folder, "01.flac")).Should().Be(Music("Artist", folder));
    }

    private sealed record AlbumFile(string Path, int Disc);

    [Fact]
    public void Two_folders_of_the_same_title_with_the_same_discs_are_two_albums()
    {
        var albums = MusicAlbumFolders.Albums<AlbumFile>(
        [
            new(Music("Weezer", "Weezer (Blue Album)", "01.flac"), 1),
            new(Music("Weezer", "Weezer (Teal Album)", "01.flac"), 1),
            new(Music("Weezer", "Weezer (Teal Album)", "02.flac"), 1),
        ], f => f.Path, f => f.Disc);

        albums.Should().HaveCount(2);
    }

    [Fact]
    public void Sibling_folders_that_each_hold_their_own_disc_are_one_album()
    {
        var albums = MusicAlbumFolders.Albums<AlbumFile>(
        [
            new(Music("The Beatles", "The Beatles (Disc 1)", "01.flac"), 1),
            new(Music("The Beatles", "The Beatles (Disc 2)", "01.flac"), 2),
        ], f => f.Path, f => f.Disc);

        albums.Should().ContainSingle();
    }

    [Fact]
    public void Sibling_folders_without_disc_tags_are_not_assumed_to_be_discs()
    {
        var albums = MusicAlbumFolders.Albums<AlbumFile>(
        [
            new(Music("Ariana Grande", "positions", "01.flac"), 0),
            new(Music("Ariana Grande", "Positions", "01.flac"), 0),
        ], f => f.Path, f => f.Disc);

        albums.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("Don't Push", "Don’t Push")]
    [InlineData("Don't Push", "Dont Push")]
    [InlineData("Knock-Down Drag-Out", "Knock‐Down Drag‐Out")]
    [InlineData("Wonderful Wonderful", "wonderful  wonderful")]
    [InlineData("C.E.O", "C E O")]
    public void The_same_title_written_differently_is_the_same_song(string left, string right)
    {
        TrackTitles.Same(left, right).Should().BeTrue();
    }

    [Theory]
    [InlineData("Circus", "Circus (acoustic version)")]
    [InlineData("Sugar", "Sugar (extended mix)")]
    [InlineData("", "")]
    public void A_different_title_is_a_different_song(string left, string right)
    {
        TrackTitles.Same(left, right).Should().BeFalse();
    }

    [Theory]
    [InlineData("Neon Pill.mp3", "flac", "mp3")]
    [InlineData("HiFi.flac", "mp3", "flac")]
    [InlineData("Song.m4a", "alac", "alac")]
    [InlineData("Song.m4a", "flac", "aac")]
    [InlineData("Song.wav", "flac", "pcm")]
    public void A_duplicate_is_scored_by_its_own_format_not_its_tracks(string fileName, string trackCodec, string scored)
    {
        var part = new MediaPart { FilePath = Music("Artist", "Album", fileName), Container = Path.GetExtension(fileName).TrimStart('.') };

        MediaDedupeManager.FileCodec(part, new Track { Title = "Song", AudioCodec = trackCodec }).Should().Be(scored);
    }
}
