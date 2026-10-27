using Microsoft.Extensions.Hosting;

namespace Vora.Infrastructure.Tests;

internal sealed class TestLifetime : IHostApplicationLifetime, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();

    public CancellationToken ApplicationStarted => CancellationToken.None;

    public CancellationToken ApplicationStopping => _stopping.Token;

    public CancellationToken ApplicationStopped => CancellationToken.None;

    public void StopApplication() => _stopping.Cancel();

    public void Dispose() => _stopping.Dispose();
}
