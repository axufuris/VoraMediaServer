using Microsoft.Extensions.DependencyInjection;
using Vora.Application.Analysis;
using Vora.Application.Media.Ai;
using Vora.Application.Tasks;

namespace Vora.Application.Tests.Tasks;

public class AiTaskResilienceTests
{
    private readonly TaskQueueManager _queue = new(Substitute.For<IClientNotifier>());

    private async Task<Vora.Application.Tasks.Dtos.QueuedTaskDto> Next()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await foreach (var task in _queue.DequeueAsync(cts.Token)) return task;
        throw new InvalidOperationException("nothing was queued");
    }

    [Fact]
    public async Task Weekly_ai_playlists_are_still_made_when_preparing_new_songs_fails()
    {
        var embeddings = Substitute.For<IMusicEmbeddingService>();
        embeddings.PrepareTracksAsync(Arg.Any<CancellationToken>()).Returns<Task<int>>(_ => throw new InvalidOperationException("insert failed"));
        var playlists = Substitute.For<IAiPlaylistService>();
        var sp = new ServiceCollection().AddSingleton(embeddings).AddSingleton(playlists).BuildServiceProvider();

        _queue.QueueGenerateAiPlaylists();
        await (await Next()).WorkItem(CancellationToken.None, sp);

        await playlists.Received(1).GenerateWeeklyForDueProfilesAsync(false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_second_embedding_run_is_not_queued_while_one_is_waiting()
    {
        _queue.QueueGenerateAiEmbeddings();
        _queue.QueueGenerateAiEmbeddings();

        _queue.GetAllTasks().Should().ContainSingle();
    }
}
