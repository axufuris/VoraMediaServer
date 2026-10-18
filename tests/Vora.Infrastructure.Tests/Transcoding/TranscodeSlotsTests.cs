using Vora.Infrastructure.Transcoding;

namespace Vora.Infrastructure.Tests.Transcoding;

public class TranscodeSlotsTests
{
    private static readonly TranscodeSlots.Running Copy = new(EncodesVideo: false, OnGpu: false);
    private static readonly TranscodeSlots.Running CpuEncode = new(EncodesVideo: true, OnGpu: false);
    private static readonly TranscodeSlots.Running GpuEncode = new(EncodesVideo: true, OnGpu: true);

    [Fact]
    public void A_copy_takes_no_slot_however_full_the_server_is()
    {
        var slot = TranscodeSlots.Assign(encodesVideo: false, gpuReady: true, [GpuEncode, CpuEncode], maxGpu: 1, maxCpu: 1);

        slot.Should().Be(new TranscodeSlots.Assignment(OnGpu: false, Refusal: null));
    }

    [Fact]
    public void Copies_running_do_not_count_against_a_transcode()
    {
        TranscodeSlots.Assign(encodesVideo: true, gpuReady: false, [Copy, Copy, Copy], maxGpu: 0, maxCpu: 1).Refusal.Should().BeNull();
        TranscodeSlots.Assign(encodesVideo: true, gpuReady: true, [Copy, Copy], maxGpu: 1, maxCpu: 1).OnGpu.Should().BeTrue();
    }

    [Fact]
    public void A_transcode_goes_to_the_gpu_while_it_has_room_then_to_the_cpu_then_is_refused()
    {
        TranscodeSlots.Assign(true, gpuReady: true, [], maxGpu: 1, maxCpu: 1).OnGpu.Should().BeTrue();
        TranscodeSlots.Assign(true, gpuReady: true, [GpuEncode], maxGpu: 1, maxCpu: 1).Should().Be(new TranscodeSlots.Assignment(false, null));
        TranscodeSlots.Assign(true, gpuReady: true, [GpuEncode, CpuEncode], maxGpu: 1, maxCpu: 1).Refusal.Should().NotBeNull();
        TranscodeSlots.Assign(true, gpuReady: false, [CpuEncode], maxGpu: 0, maxCpu: 1).Refusal.Should().Be("Maximum CPU transcodes reached.");
    }

    [Fact]
    public void No_limit_means_no_refusal()
    {
        TranscodeSlots.Assign(true, gpuReady: false, [CpuEncode, CpuEncode, CpuEncode], maxGpu: 0, maxCpu: 0).Refusal.Should().BeNull();
        TranscodeSlots.Assign(true, gpuReady: true, [GpuEncode, GpuEncode], maxGpu: 0, maxCpu: 1).OnGpu.Should().BeTrue();
    }
}
