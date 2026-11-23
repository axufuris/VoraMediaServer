using Vora.Application.Streaming;
using Vora.Infrastructure.Transcoding;

namespace Vora.Infrastructure.Tests.Transcoding;

public class DvrEncodesTests
{
    [Fact]
    public void A_conversion_counts_while_it_runs_and_not_after_it_is_disposed()
    {
        var encodes = new DvrEncodes();
        var conversion = encodes.Begin(Guid.NewGuid(), onGpu: true);

        encodes.RunningEncodes(Guid.NewGuid()).Should().Equal(new RunningEncode(OnGpu: true));

        conversion.Dispose();
        conversion.Dispose();
        encodes.RunningEncodes(Guid.NewGuid()).Should().BeEmpty();
    }

    [Fact]
    public void A_conversion_that_fell_back_to_the_cpu_holds_a_cpu_slot_not_a_gpu_one()
    {
        var encodes = new DvrEncodes();
        using var conversion = encodes.Begin(Guid.NewGuid(), onGpu: true);

        conversion.MovedToCpu();

        encodes.RunningEncodes(Guid.NewGuid()).Should().Equal(new RunningEncode(OnGpu: false));
    }

    [Fact]
    public void The_conversion_asking_is_left_out_of_the_count()
    {
        var encodes = new DvrEncodes();
        var key = Guid.NewGuid();
        using var conversion = encodes.Begin(key, onGpu: false);

        encodes.RunningEncodes(key).Should().BeEmpty();
    }
}
