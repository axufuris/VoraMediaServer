namespace Vora.Application.Maintenance;

public interface IStorageReferenceRepository
{
    Task<Dictionary<string, HashSet<string>>> FindReferencedNamesAsync(IReadOnlyCollection<string> prefixes, CancellationToken cancellationToken = default);
    Task<HashSet<Guid>> GetMediaItemIdsAsync(CancellationToken cancellationToken = default);
    Task<HashSet<Guid>> GetMediaPartIdsAsync(CancellationToken cancellationToken = default);
    Task<SubtitleFileReferences> GetSubtitleFileReferencesAsync(CancellationToken cancellationToken = default);
    Task<List<string>> GetOriginalPosterUrlsAsync(CancellationToken cancellationToken = default);
    Task<List<string>> GetRecordingFilePathsAsync(CancellationToken cancellationToken = default);
}

public sealed record SubtitleFileReferences(HashSet<Guid> TrackIds, List<string> FilePaths);
