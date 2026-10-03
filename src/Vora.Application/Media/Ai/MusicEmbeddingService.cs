using Microsoft.Extensions.Logging;
using Vora.Application.Ai;
using Vora.Application.Settings;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Media.Ai;

public interface IMusicEmbeddingService
{
    Task<int> PrepareTracksAsync(CancellationToken cancellationToken);
}

public class MusicEmbeddingService : IMusicEmbeddingService
{
    public const string PluginId = "openai_music_playlists";
    public const int BatchSize = 256;

    private readonly IMusicRepository _repository;
    private readonly ITrackProfileService _profiles;
    private readonly IOpenAiClient _openAi;
    private readonly ISystemSettingsRepository _settings;
    private readonly ITaskProgressReporter _progress;
    private readonly ILogger<MusicEmbeddingService> _logger;

    public MusicEmbeddingService(IMusicRepository repository, ITrackProfileService profiles, IOpenAiClient openAi, ISystemSettingsRepository settings, ITaskProgressReporter progress, ILogger<MusicEmbeddingService> logger)
    {
        _repository = repository;
        _profiles = profiles;
        _openAi = openAi;
        _settings = settings;
        _progress = progress;
        _logger = logger;
    }

    public async Task<int> PrepareTracksAsync(CancellationToken cancellationToken)
    {
        var server = await _settings.GetSettingsAsync();
        if (!server.EnableAiMusicPlaylists || !await _openAi.IsConfiguredAsync()) return 0;

        await _profiles.ProfileTracksAsync(cancellationToken);

        var tags = await _repository.GetAllArtistTagNamesAsync(TrackProfileService.TagsPerArtist);
        var due = (await _repository.GetTrackDescriptorsAsync())
            .Select(t => (Track: t, Text: SongProfile.Describe(t, ArtistTags(t, tags))))
            .Select(x => (x.Track, x.Text, Hash: SongProfile.Fingerprint(x.Text)))
            .Where(x => x.Hash != x.Track.EmbeddedHash || x.Track.EmbeddedModel != OpenAiClient.EmbeddingModel)
            .ToList();

        var done = 0;
        foreach (var batch in due.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            _progress.Report($"Preparing music for AI playlists {done:N0}/{due.Count:N0}");

            var vectors = await _openAi.EmbedAsync(PluginId, batch.Select(x => x.Text).ToList(), cancellationToken);
            if (vectors == null) break;

            var updates = batch
                .Select((x, i) => (x.Track.TrackId, x.Hash, Vector: i < vectors.Count ? vectors[i] : null))
                .Where(x => x.Vector != null)
                .Select(x => new TrackEmbeddingUpdate(x.TrackId, x.Vector ?? Array.Empty<float>(), x.Hash, OpenAiClient.EmbeddingModel))
                .ToList();
            if (updates.Count == 0) break;

            done += await _repository.SaveTrackEmbeddingsAsync(updates);
        }

        _progress.Report(null);
        if (done > 0) _logger.LogInformation("Embedded {Count} song(s) for AI playlists.", done);
        return done;
    }

    private static IReadOnlyList<string> ArtistTags(TrackDescriptor track, IReadOnlyDictionary<Guid, List<string>> tags) =>
        track.AlbumArtistId is Guid artistId && tags.TryGetValue(artistId, out var found) ? found : Array.Empty<string>();
}
