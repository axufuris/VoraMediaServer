using Vora.Application.Streaming;
using Vora.Infrastructure.Transcoding;

namespace Vora.Infrastructure.Tests.Transcoding;

public class EncodeSlotsTests
{
    private static readonly RunningEncode Gpu = new(OnGpu: true);
    private static readonly RunningEncode Cpu = new(OnGpu: false);

    private sealed class Source(params RunningEncode[] encodes) : IEncodeSource
    {
        public Guid? AskedBy { get; private set; }

        public IReadOnlyList<RunningEncode> RunningEncodes(Guid except)
        {
            AskedBy = except;
            return encodes;
        }
    }

    [Fact]
    public void Every_place_that_encodes_counts_toward_the_same_limits()
    {
        var playback = new Source(Gpu);
        var live = new Source(Gpu);
        var recordings = new Source(Cpu);
        var slots = new EncodeSlots([playback, live, recordings]);

        slots.Assign(Guid.NewGuid(), gpuReady: true, maxGpu: 2, maxCpu: 0).Should().Be(new EncodeSlot(OnGpu: false, Refusal: null), "playback and live TV already hold both GPU slots");
        slots.Assign(Guid.NewGuid(), gpuReady: true, maxGpu: 3, maxCpu: 0).OnGpu.Should().BeTrue();
        slots.Assign(Guid.NewGuid(), gpuReady: true, maxGpu: 2, maxCpu: 1).Refusal.Should().Be("Maximum GPU and CPU transcodes reached.", "the recording being converted holds the one CPU slot");
    }

    [Fact]
    public void An_encode_asking_again_is_not_counted_against_itself()
    {
        var key = Guid.NewGuid();
        var source = new Source();

        new EncodeSlots([source]).Assign(key, gpuReady: true, maxGpu: 1, maxCpu: 1).OnGpu.Should().BeTrue();

        source.AskedBy.Should().Be(key, "each source leaves out the encode that is asking");
    }

    [Fact]
    public void With_nothing_running_and_a_gpu_ready_the_encode_goes_to_the_gpu_and_otherwise_the_cpu()
    {
        var slots = new EncodeSlots([]);

        slots.Assign(Guid.NewGuid(), gpuReady: true, maxGpu: 1, maxCpu: 1).OnGpu.Should().BeTrue();
        slots.Assign(Guid.NewGuid(), gpuReady: false, maxGpu: 1, maxCpu: 1).Should().Be(new EncodeSlot(false, null));
    }

    [Fact]
    public async Task Only_one_start_at_a_time_picks_a_slot_so_two_cannot_take_the_last_one()
    {
        var slots = new EncodeSlots([]);
        var first = await slots.HoldAsync(TestContext.Current.CancellationToken);

        var second = slots.HoldAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        second.IsCompleted.Should().BeFalse("the first start still holds the slots");

        first.Dispose();
        first.Dispose();
        using var held = await second.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var third = slots.HoldAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        third.IsCompleted.Should().BeFalse("disposing a hold twice must not let two starts through");
    }

    [Fact]
    public async Task A_start_that_gives_up_waiting_does_not_leave_the_slots_held()
    {
        var slots = new EncodeSlots([]);
        using var first = await slots.HoldAsync(TestContext.Current.CancellationToken);
        using var giveUp = new CancellationTokenSource(50);

        var waiting = async () => await slots.HoldAsync(giveUp.Token);
        await waiting.Should().ThrowAsync<OperationCanceledException>();

        first.Dispose();
        using var next = await slots.HoldAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
}
