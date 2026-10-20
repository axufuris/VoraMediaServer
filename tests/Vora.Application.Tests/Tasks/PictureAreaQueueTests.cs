using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Vora.Application.Analysis;
using Vora.Application.Libraries;
using Vora.Application.Posters;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Library;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Tasks;

public class PictureAreaQueueTests
{
    private static readonly Guid Library = Guid.NewGuid();
    private readonly ITaskQueueManager _queue = Substitute.For<ITaskQueueManager>();

    private ServiceProvider Services(LibraryType type)
    {
        var libraries = Substitute.For<ILibraryRepository>();
        libraries.GetProjectedByIdAsync(Arg.Any<Guid>(), Arg.Any<Expression<Func<MediaLibrary, LibraryType?>>>()).Returns(type);
        return new ServiceCollection()
            .AddSingleton(Substitute.For<IMediaAnalyzerManager>())
            .AddSingleton(Substitute.For<IPosterOverlayManager>())
            .AddSingleton(libraries)
            .AddSingleton(_queue)
            .AddSingleton<ITaskProgressReporter>(new NullTaskProgressReporter())
            .BuildServiceProvider();
    }

    [Theory]
    [InlineData(LibraryAnalysisReason.Schedule)]
    [InlineData(LibraryAnalysisReason.Manual)]
    public async Task Every_analysis_of_a_video_library_looks_for_black_bars_not_only_the_one_after_a_scan(LibraryAnalysisReason reason)
    {
        await using var services = Services(LibraryType.TvShow);

        await TaskQueueManager.RunLibraryAnalysisAsync(services, Library, "Shows", reason, null, TestContext.Current.CancellationToken);

        _queue.Received(1).QueueDetectLibraryPictureArea(Library, "Shows");
    }

    [Fact]
    public async Task A_music_library_is_not_looked_at()
    {
        await using var services = Services(LibraryType.Music);

        await TaskQueueManager.RunLibraryAnalysisAsync(services, Library, "Music", LibraryAnalysisReason.Schedule, null, TestContext.Current.CancellationToken);

        _queue.DidNotReceiveWithAnyArgs().QueueDetectLibraryPictureArea(default);
    }
}
