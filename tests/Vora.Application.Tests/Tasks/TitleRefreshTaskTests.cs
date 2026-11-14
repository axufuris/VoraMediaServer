using Microsoft.Extensions.DependencyInjection;
using Vora.Application.Analysis;
using Vora.Application.Metadata;
using Vora.Application.Posters;
using Vora.Application.Tasks;

namespace Vora.Application.Tests.Tasks;

public class TitleRefreshTaskTests
{
    private readonly TaskQueueManager _queue = new(Substitute.For<IClientNotifier>(), Substitute.For<ITaskJournal>());
    private readonly IMetadataManager _metadata = Substitute.For<IMetadataManager>();
    private readonly IPosterOverlayManager _overlays = Substitute.For<IPosterOverlayManager>();

    private async Task RunNextAsync()
    {
        var sp = new ServiceCollection().AddSingleton(_metadata).AddSingleton(_overlays).BuildServiceProvider();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await foreach (var task in _queue.DequeueAsync(cts.Token))
        {
            await task.WorkItem(CancellationToken.None, sp);
            return;
        }
    }

    [Fact]
    public async Task The_title_menus_refresh_takes_the_one_per_title_path_once()
    {
        var show = Guid.NewGuid();
        _queue.QueueRefreshMediaItemMetadata(show, "The Wire", forceOverride: true);

        await RunNextAsync();

        await _metadata.Received(1).RefreshTitleAsync(show, true, Arg.Any<CancellationToken>());
        await _metadata.DidNotReceive().TriggerMediaItemMetadataRefreshAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _metadata.DidNotReceive().TriggerMediaItemArtworkRefreshAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _overlays.Received(1).GenerateOverlaysForMediaAsync(show, Arg.Any<CancellationToken>());
    }
}
