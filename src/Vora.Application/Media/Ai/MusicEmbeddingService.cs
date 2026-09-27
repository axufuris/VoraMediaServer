using Microsoft.Extensions.Logging;
using Vora.Application.Ai;
using Vora.Application.Settings;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Media.Ai;

public interface IMusicEmbeddingService
{
    Task<int> EmbedMissingTracksAsync(CancellationToken cancellationToken);
}

// One vector per song, so AI playlists can find songs by meaning with a
// database query instead of asking a chat model to read the library. A song is
// embedded once - about forty tokens, so a 100,000-song library costs around
// twenty cents - and never again unless it is new.
//
// Only while an admin has AI playlists switched on: a server that never uses
// them never pays for them.
public class MusicEmbeddingService : IMusicEmbeddingService
{
    public const string PluginId = "openai_music_playlists";

    // OpenAI takes up to 2,048 inputs a request; smaller batches keep one
    // failure cheap and the progress line moving.
    public const int BatchSize = 256;

    private readonly IMusicRepository _repository;
    private readonly IOpenAiClient _openAi;
    private readonly ISystemSettingsRepository _settings;
    private readonly ITaskProgressReporter _progress;
    private readonly ILogger<MusicEmbeddingService> _logger;

    public MusicEmbeddingService(IMusicRepository repository, IOpenAiClient openAi, ISystemSettingsRepository settings, ITaskProgressReporter progress, ILogger<MusicEmbeddingService> logger)
    {
        _repository = repository;
        _openAi = openAi;
        _settings = settings;
        _progress = progress;
        _logger = logger;
    }

    public async Task<int> EmbedMissingTracksAsync(CancellationToken cancellationToken)
    {
        var server = await _settings.GetSettingsAsync();
        if (!server.EnableAiMusicPlaylists || !await _openAi.IsConfiguredAsync()) return 0;

        var total = await _repository.CountTracksMissingEmbeddingsAsync();
        var done = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await _repository.GetTracksMissingEmbeddingsAsync(BatchSize);
            if (batch.Count == 0) break;

            _progress.Report($"Preparing music for AI playlists {done:N0}/{total:N0}");

            var vectors = await _openAi.EmbedAsync(PluginId, batch.Select(Describe).ToList(), cancellationToken);
            if (vectors == null) break;

            var saved = batch
                .Select((track, i) => (track.TrackId, Vector: vectors[i]))
                .Where(x => x.Vector != null)
                .Select(x => (x.TrackId, x.Vector!))
                .ToList();

            // Nothing came back for this batch: stop rather than pay for the same
            // songs again on the next pass.
            if (saved.Count == 0) break;

            await _repository.SaveTrackEmbeddingsAsync(saved);
            done += saved.Count;
        }

        _progress.Report(null);
        if (done > 0) _logger.LogInformation("Embedded {Count} song(s) for AI playlists.", done);
        return done;
    }

    // What the model reads for a song. The title and artist carry most of the
    // meaning - the model already knows most released songs by name - and the
    // album, year and genre place the rest.
    internal static string Describe(TrackForEmbedding t)
    {
        var parts = new List<string> { $"Song: {t.Title}" };
        if (!string.IsNullOrWhiteSpace(t.Artist)) parts.Add($"Artist: {t.Artist}");
        if (!string.IsNullOrWhiteSpace(t.AlbumTitle)) parts.Add(t.Year is int y ? $"Album: {t.AlbumTitle} ({y})" : $"Album: {t.AlbumTitle}");
        if (!string.IsNullOrWhiteSpace(t.Genre)) parts.Add($"Genre: {t.Genre}");
        return string.Join(". ", parts);
    }
}
