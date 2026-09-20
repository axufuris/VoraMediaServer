using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Email;
using Vora.Domain.Entities.Email;
using Vora.Domain.Enums;
using Vora.Infrastructure.Workers;

namespace Vora.Infrastructure.Tests.Workers;

public class EmailDispatchWorkerTests
{
    private sealed class TestQueue : IEmailDispatchQueue
    {
        private readonly Channel<QueuedEmail> _channel = Channel.CreateUnbounded<QueuedEmail>();
        public ChannelReader<QueuedEmail> Reader => _channel.Reader;
        public ValueTask EnqueueAsync(QueuedEmail email, CancellationToken cancellationToken = default) =>
            _channel.Writer.WriteAsync(email, cancellationToken);
        public void Complete() => _channel.Writer.TryComplete();
    }

    private sealed class FakeScope : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; }
        public FakeScope(IServiceProvider sp) { ServiceProvider = sp; }
        public void Dispose() { }
    }

    private sealed class FakeScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceProvider _sp;
        public FakeScopeFactory(IServiceProvider sp) { _sp = sp; }
        public IServiceScope CreateScope() => new FakeScope(_sp);
    }

    private static QueuedEmail MakeEmail() => new()
    {
        LogId = Guid.NewGuid(),
        TemplateKey = EmailTemplateKey.PasswordReset,
        ToAddress = "user@example.com",
        Subject = "Reset",
        HtmlBody = "<p>html</p>",
        TextBody = "text"
    };

    private static (EmailDispatchWorker worker, TestQueue queue, IEmailTransport transport, IEmailDeliveryLogRepository logRepo) Build()
    {
        var queue = new TestQueue();
        var transport = Substitute.For<IEmailTransport>();
        var logRepo = Substitute.For<IEmailDeliveryLogRepository>();
        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(IEmailTransport)).Returns(transport);
        sp.GetService(typeof(IEmailDeliveryLogRepository)).Returns(logRepo);
        var worker = new EmailDispatchWorker(queue, new FakeScopeFactory(sp), NullLogger<EmailDispatchWorker>.Instance);
        return (worker, queue, transport, logRepo);
    }

    [Fact]
    public async Task ExecuteAsync_sends_email_and_marks_log_as_sent_on_success()
    {
        var (worker, queue, transport, logRepo) = Build();
        var email = MakeEmail();
        transport.SendAsync(Arg.Any<QueuedEmail>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await queue.EnqueueAsync(email, TestContext.Current.CancellationToken);
        queue.Complete();

        await (worker.ExecuteTask ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        await transport.Received(1).SendAsync(Arg.Is<QueuedEmail>(e => e.LogId == email.LogId), Arg.Any<CancellationToken>());
        await logRepo.Received(1).UpdateAsync(
            email.LogId,
            EmailDeliveryStatus.Sent,
            1,
            null,
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_retries_and_succeeds_records_attempt_count()
    {
        var (worker, queue, transport, logRepo) = Build();
        var email = MakeEmail();

        var calls = 0;
        transport.SendAsync(Arg.Any<QueuedEmail>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls++;
            if (calls == 1) throw new InvalidOperationException("transient");
            return Task.CompletedTask;
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await worker.StartAsync(cts.Token);
        await queue.EnqueueAsync(email, TestContext.Current.CancellationToken);
        queue.Complete();

        await (worker.ExecuteTask ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        calls.Should().Be(2);
        await logRepo.Received(1).UpdateAsync(
            email.LogId,
            EmailDeliveryStatus.Sent,
            2,
            null,
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_does_not_write_final_log_when_cancelled_mid_retry()
    {
        var (worker, queue, transport, logRepo) = Build();
        var email = MakeEmail();

        transport.SendAsync(Arg.Any<QueuedEmail>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("transient"));

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await queue.EnqueueAsync(email, TestContext.Current.CancellationToken);

        // Wait for the first attempt to fail and the worker to enter Task.Delay
        await Task.Delay(200, TestContext.Current.CancellationToken);
        cts.Cancel();

        await (worker.ExecuteTask ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        // Cancellation during retry escapes through the inner OperationCanceledException rethrow,
        // so the final Failed log write does NOT happen — by design (clean shutdown).
        await logRepo.DidNotReceive().UpdateAsync(
            email.LogId,
            EmailDeliveryStatus.Failed,
            Arg.Any<int>(),
            Arg.Any<string?>(),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_processes_multiple_emails_sequentially()
    {
        var (worker, queue, transport, logRepo) = Build();
        transport.SendAsync(Arg.Any<QueuedEmail>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var a = MakeEmail();
        var b = MakeEmail();
        var c = MakeEmail();

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await queue.EnqueueAsync(a, TestContext.Current.CancellationToken);
        await queue.EnqueueAsync(b, TestContext.Current.CancellationToken);
        await queue.EnqueueAsync(c, TestContext.Current.CancellationToken);
        queue.Complete();

        await (worker.ExecuteTask ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        await transport.Received(3).SendAsync(Arg.Any<QueuedEmail>(), Arg.Any<CancellationToken>());
        await logRepo.Received(1).UpdateAsync(a.LogId, EmailDeliveryStatus.Sent, 1, null, Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        await logRepo.Received(1).UpdateAsync(b.LogId, EmailDeliveryStatus.Sent, 1, null, Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        await logRepo.Received(1).UpdateAsync(c.LogId, EmailDeliveryStatus.Sent, 1, null, Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }
}
