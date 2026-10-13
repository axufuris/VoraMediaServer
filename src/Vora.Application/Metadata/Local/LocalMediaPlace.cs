using Vora.Application.Media.Dtos;
using Vora.Plugins.Dtos;
using Vora.Plugins.Providers.Local;

namespace Vora.Application.Metadata.Local;

public sealed class LocalMediaPlace
{
    private LocalMediaPlace(LocalMediaSourceDto source, IReadOnlyList<string> paths, IReadOnlyList<string> directories, IReadOnlyList<string> mediaFiles)
    {
        Source = source;
        Paths = paths;
        Directories = directories;
        MediaFiles = mediaFiles;
    }

    public LocalMediaSourceDto Source { get; }
    public IReadOnlyList<string> Paths { get; }
    public IReadOnlyList<string> Directories { get; }
    public IReadOnlyList<string> MediaFiles { get; }

    public static LocalMediaPlace? From(LocalMediaSourceDto source, LocalMediaFolders folders)
    {
        var files = source.FilePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0) return null;

        switch (source.Kind)
        {
            case LocalMediaSourceDto.Movie:
            case LocalMediaSourceDto.Episode:
                return new LocalMediaPlace(source, files, Distinct(files.Select(Path.GetDirectoryName)), files);

            case LocalMediaSourceDto.Season:
                var seasonDirectories = Distinct(files.Select(Path.GetDirectoryName));
                var showsOfSeason = Distinct(files.Select(file => LocalMediaAssets.ShowDirectory(file, folders)));
                return new LocalMediaPlace(source, seasonDirectories, Distinct(seasonDirectories.Concat(showsOfSeason)), []);

            case LocalMediaSourceDto.TvShow:
                var showDirectories = Distinct(files.Select(file => LocalMediaAssets.ShowDirectory(file, folders)));
                return new LocalMediaPlace(source, showDirectories, showDirectories, []);

            default:
                return null;
        }
    }

    public MetadataResult? ReadDocument(LocalMediaFolders folders) => Source.Kind switch
    {
        LocalMediaSourceDto.Movie => First(path => LocalNfoReader.ReadMovie(path, folders)),
        LocalMediaSourceDto.TvShow => First(path => LocalNfoReader.ReadTvShow(path, folders)),
        LocalMediaSourceDto.Season => Source.SeasonNumber is int season
            ? First(path => LocalNfoReader.ReadSeason(path, season, folders))
            : null,
        LocalMediaSourceDto.Episode => Source.SeasonNumber is int season && Source.EpisodeNumber is int episode
            ? First(path => LocalNfoReader.ReadEpisode(path, season, episode, folders))
            : null,
        _ => null
    };

    public IReadOnlyList<string> Documents(LocalMediaFolders folders) => Source.Kind switch
    {
        LocalMediaSourceDto.Movie => Paths.SelectMany(path => LocalNfoReader.MovieDocuments(path, folders)).Distinct(StringComparer.Ordinal).ToList(),
        LocalMediaSourceDto.TvShow => Named(LocalMediaAssets.ShowDocument, folders),
        LocalMediaSourceDto.Season => Named(LocalMediaAssets.SeasonDocument, folders),
        LocalMediaSourceDto.Episode => Paths.Select(path => LocalNfoReader.Sidecar(path, folders)).OfType<string>().ToList(),
        _ => []
    };

    public IReadOnlyList<ArtworkResult> FindArtwork(LocalMediaFolders folders) =>
        Paths
            .SelectMany(path => LocalMediaAssets.FindArtwork(Source.Kind, path, Source.SeasonNumber, folders))
            .DistinctBy(result => result.Kind)
            .ToList();

    public ExternalIdSet TaggedIds(LocalMediaFolders folders) => Source.Kind switch
    {
        LocalMediaSourceDto.Movie => LocalIdentity.TaggedIds(Paths.SelectMany(path => MovieNames(path, folders)).ToArray()),
        LocalMediaSourceDto.TvShow => LocalIdentity.TaggedIds(Paths.Select(Path.GetFileName).ToArray()),
        _ => ExternalIdSet.Empty
    };

    public int? ParsedYear() => Source.Kind switch
    {
        LocalMediaSourceDto.Movie => Paths.Select(path => LocalIdentity.YearIn(Path.GetFileNameWithoutExtension(path))).FirstOrDefault(year => year != null),
        LocalMediaSourceDto.TvShow => Paths.Select(path => LocalIdentity.YearIn(Path.GetFileName(path))).FirstOrDefault(year => year != null),
        _ => null
    };

    public string? ParsedTitle() => Source.Kind switch
    {
        LocalMediaSourceDto.Movie => LocalIdentity.TitleIn(Path.GetFileNameWithoutExtension(Paths[0])),
        LocalMediaSourceDto.TvShow => LocalIdentity.TitleIn(Path.GetFileName(Paths[0])),
        _ => null
    };

    public string? Resolve(string fileName, LocalMediaFolders folders)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains('/') || fileName.Contains('\\')) return null;
        if (!LocalMediaAssets.IsArtworkName(fileName, Source.SeasonNumber, MediaFiles)) return null;

        return Directories
            .Select(directory => LocalMediaAssets.NamedFile(folders.Files(directory), fileName))
            .FirstOrDefault(file => file != null);
    }

    private static IEnumerable<string?> MovieNames(string path, LocalMediaFolders folders)
    {
        yield return Path.GetFileNameWithoutExtension(path);

        var directory = Path.GetDirectoryName(path);
        if (LocalMediaAssets.HoldsOneFilm(folders.Files(directory), path)) yield return Path.GetFileName(directory);
    }

    private MetadataResult? First(Func<string, MetadataResult?> read) =>
        Paths.Select(read).FirstOrDefault(result => result != null);

    private List<string> Named(string name, LocalMediaFolders folders) =>
        Paths.Select(path => LocalMediaAssets.NamedFile(folders.Files(path), name)).OfType<string>().ToList();

    private static List<string> Distinct(IEnumerable<string?> values) =>
        values.OfType<string>().Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
}
