using Microsoft.EntityFrameworkCore;
using Vora.Application.Metadata;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Backups.Sections;

namespace Vora.Infrastructure.Tests.Backups;

public sealed class MediaEditsRestoreTests : IDisposable
{
    private const string MatrixFile = "/media/Movies/The Matrix (1999)/The Matrix (1999).mkv";
    private const string PilotFile = "/media/TV Shows/Breaking Bad/Season 01/Breaking Bad - S01E02.mkv";
    private const string TrackFile = "/media/Music/Radiohead/OK Computer/02 - Paranoid Android.flac";
    private readonly string _subtitles = Path.Combine(Path.GetTempPath(), "vora-subs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_subtitles)) Directory.Delete(_subtitles, recursive: true);
    }

    private static void AddFiles(BackupTestWorld world)
    {
        world.Db.MediaParts.AddRange(
            new MediaPart { FilePath = MatrixFile, MediaItemId = world.Matrix },
            new MediaPart { FilePath = PilotFile, MediaItemId = world.Pilot },
            new MediaPart { FilePath = TrackFile, MediaItemId = world.ParanoidAndroid });
        world.Db.SaveChanges();
        world.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Hand_made_changes_follow_their_files_to_a_rebuilt_server()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        AddFiles(source);
        AddFiles(target);
        var ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_subtitles);
        var subtitleFile = Path.Combine(_subtitles, "matrix.en.vtt");
        await File.WriteAllTextAsync(subtitleFile, "WEBVTT", ct);

        var matrix = await source.Db.MediaItems.SingleAsync(m => m.Id == source.Matrix, ct);
        matrix.Title = "The Matrix (Director's Cut)";
        matrix.Overview = "Edited by hand.";
        matrix.TmdbId = "604";
        matrix.LockedFields = new List<string> { nameof(MediaItem.Title), nameof(MediaItem.Overview), MediaEditsBackupSection.MarkersLock, MediaMatchManager.MatchLock };
        var pilot = await source.Db.Set<Episode>().SingleAsync(e => e.Id == source.Pilot, ct);
        var season = await source.Db.Set<Season>().SingleAsync(s => s.Id == pilot.SeasonId, ct);
        season.Title = "The First Season";
        season.LockedFields = new List<string> { nameof(Season.Title) };
        var show = await source.Db.Set<TvShow>().SingleAsync(s => s.Id == season.TvShowId, ct);
        show.Overview = "A chemistry teacher.";
        show.LockedFields = new List<string> { nameof(TvShow.Overview) };
        var album = await source.Db.Albums.SingleAsync(a => a.Id == source.OkComputer, ct);
        album.Title = "OK Computer OKNOTOK";
        album.LockedFields = new List<string> { nameof(Album.Title) };
        var artist = await source.Db.Artists.SingleAsync(a => a.Id == source.Radiohead, ct);
        artist.Biography = "From Abingdon.";
        artist.LockedFields = new List<string> { nameof(Artist.Biography) };
        var gone = await source.Db.MediaItems.SingleAsync(m => m.Id == source.OnlyOnSource, ct);
        gone.LockedFields = new List<string> { nameof(MediaItem.Title) };
        source.Db.MediaItemMarkers.AddRange(
            new MediaItemMarker { MediaItemId = source.Matrix, Type = MarkerType.Intro, Start = TimeSpan.FromSeconds(10), End = TimeSpan.FromSeconds(70) },
            new MediaItemMarker { MediaItemId = source.Matrix, Type = MarkerType.Credits, Start = TimeSpan.FromMinutes(128), End = TimeSpan.FromMinutes(136) });
        source.Db.MediaArtwork.Add(new MediaArtwork { MediaItemId = source.Matrix, Url = "/api/artwork/custom/media_matrix_poster.jpg", Kind = ArtworkKind.Poster, IsUserUploaded = true, ProviderId = "upload" });
        var sourcePart = await source.Db.MediaParts.SingleAsync(p => p.FilePath == MatrixFile, ct);
        source.Db.MediaSubtitleTracks.Add(new MediaSubtitleTrack { MediaPartId = sourcePart.Id, ExternalFilePath = subtitleFile, IsDownloaded = true, Language = "English", Codec = "webvtt" });
        await source.Db.SaveChangesAsync(ct);

        target.Db.MediaItemMarkers.Add(new MediaItemMarker { MediaItemId = target.Matrix, Type = MarkerType.Intro, Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(5) });
        var wrongMatch = await target.Db.MediaItems.SingleAsync(m => m.Id == target.Matrix, ct);
        wrongMatch.TmdbId = "999999";
        await target.Db.SaveChangesAsync(ct);
        target.Db.ChangeTracker.Clear();

        var (_, result) = await BackupTestWorld.CopyAsync(
            new MediaEditsBackupSection(source.Db, source.References),
            new MediaEditsBackupSection(target.Db, target.References));

        var restored = await target.Db.MediaItems.AsNoTracking().SingleAsync(m => m.Id == target.Matrix, ct);
        restored.Title.Should().Be("The Matrix (Director's Cut)");
        restored.Overview.Should().Be("Edited by hand.");
        restored.TmdbId.Should().Be("604");
        restored.LockedFields.Should().Contain(new[] { nameof(MediaItem.Title), MediaEditsBackupSection.MarkersLock, MediaMatchManager.MatchLock });
        (await target.Db.MediaItemMarkers.Where(k => k.MediaItemId == target.Matrix).Select(k => k.Type).ToListAsync(ct))
            .Should().BeEquivalentTo(new[] { MarkerType.Intro, MarkerType.Credits });
        (await target.Db.MediaArtwork.Where(a => a.MediaItemId == target.Matrix).Select(a => a.Url).ToListAsync(ct))
            .Should().Equal("/api/artwork/custom/media_matrix_poster.jpg");
        var targetPart = await target.Db.MediaParts.SingleAsync(p => p.FilePath == MatrixFile, ct);
        (await target.Db.MediaSubtitleTracks.SingleAsync(t => t.MediaPartId == targetPart.Id, ct)).ExternalFilePath.Should().Be(subtitleFile);

        var targetPilot = await target.Db.Set<Episode>().AsNoTracking().SingleAsync(e => e.Id == target.Pilot, ct);
        var targetSeason = await target.Db.Set<Season>().AsNoTracking().SingleAsync(s => s.Id == targetPilot.SeasonId, ct);
        targetSeason.Title.Should().Be("The First Season");
        (await target.Db.Set<TvShow>().AsNoTracking().SingleAsync(s => s.Id == targetSeason.TvShowId, ct)).Overview.Should().Be("A chemistry teacher.");
        (await target.Db.Albums.AsNoTracking().SingleAsync(a => a.Id == target.OkComputer, ct)).Title.Should().Be("OK Computer OKNOTOK");
        (await target.Db.Artists.AsNoTracking().SingleAsync(a => a.Id == target.Radiohead, ct)).Biography.Should().Be("From Abingdon.");

        result.Warnings.Should().Contain(w => w.StartsWith("1 edited title was skipped because its item isn't on this server."));
        result.Warnings.Should().Contain(w => w.StartsWith("1 title was matched again by hand."));
    }

    [Fact]
    public async Task A_downloaded_subtitle_whose_file_is_gone_is_skipped()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        AddFiles(source);
        AddFiles(target);
        var ct = TestContext.Current.CancellationToken;
        var part = await source.Db.MediaParts.SingleAsync(p => p.FilePath == MatrixFile, ct);
        source.Db.MediaSubtitleTracks.Add(new MediaSubtitleTrack { MediaPartId = part.Id, ExternalFilePath = Path.Combine(_subtitles, "gone.vtt"), IsDownloaded = true });
        await source.Db.SaveChangesAsync(ct);

        var (_, result) = await BackupTestWorld.CopyAsync(
            new MediaEditsBackupSection(source.Db, source.References),
            new MediaEditsBackupSection(target.Db, target.References));

        (await target.Db.MediaSubtitleTracks.CountAsync(ct)).Should().Be(0);
        result.Warnings.Should().Contain(w => w.StartsWith("1 downloaded subtitle was skipped because its file isn't on this server."));
    }

    [Fact]
    public async Task Libraries_are_added_or_updated_by_name_and_others_are_left_alone()
    {
        using var source = BackupTestWorld.Source();
        using var target = BackupTestWorld.RebuiltTarget();
        var ct = TestContext.Current.CancellationToken;
        var movies = await source.Db.MediaLibraries.SingleAsync(l => l.Id == source.MoviesLibrary, ct);
        movies.ExcludeFilters = new List<string> { ".TDARR" };
        movies.EnableCreditsDetection = true;
        var anime = new MediaLibrary { Name = "Anime", Type = LibraryType.TvShow, FolderPaths = new List<string> { "/media/Anime" }, EnableIntroDetection = false };
        source.Db.MediaLibraries.Add(anime);
        await source.Db.SaveChangesAsync(ct);
        target.Db.MediaLibraries.Add(new MediaLibrary { Name = "Home Videos", Type = LibraryType.Movie, FolderPaths = new List<string> { "/media/Home" } });
        await target.Db.SaveChangesAsync(ct);
        target.Db.ChangeTracker.Clear();

        var (_, result) = await BackupTestWorld.CopyAsync(
            new LibraryDefinitionsBackupSection(source.Db),
            new LibraryDefinitionsBackupSection(target.Db));

        var libraries = await target.Db.MediaLibraries.AsNoTracking().ToListAsync(ct);
        libraries.Select(l => l.Name).Should().BeEquivalentTo(new[] { "Movies", "TV Shows", "Music", "Anime", "Home Videos" });
        var targetMovies = libraries.Single(l => l.Name == "Movies");
        targetMovies.Id.Should().Be(target.MoviesLibrary);
        targetMovies.ExcludeFilters.Should().Equal(".TDARR");
        targetMovies.EnableCreditsDetection.Should().BeTrue();
        var targetAnime = libraries.Single(l => l.Name == "Anime");
        targetAnime.Id.Should().Be(anime.Id);
        targetAnime.EnableIntroDetection.Should().BeFalse();
        result.Warnings.Should().Equal("Added the library Anime. Scan it to bring its titles in, then restore the user-data sections again.");
    }
}
