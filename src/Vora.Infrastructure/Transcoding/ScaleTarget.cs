using Vora.Application.Streaming;
using Vora.Application.Streaming.ViewModels;

namespace Vora.Infrastructure.Transcoding;

internal readonly record struct ScaleTarget(int Width, int Height)
{
    public string Cpu => Width > 0 ? $"scale={Width}:{Height}" : $"scale=-2:{Height}";

    public string Cuda => Width > 0 ? $"{Width}:{Height}" : $"-2:{Height}";

    public string Sized => Width > 0 ? $"w={Width}:h={Height}" : $"w=-2:h={Height}";

    public static ScaleTarget? For(PlaybackDecisionVM decision, bool downscaleForHdr)
    {
        if (decision.OutputWidth > 0 && decision.OutputHeight > 0)
        {
            var size = new FrameSize(decision.OutputWidth, decision.OutputHeight);
            if (downscaleForHdr) size = OutputSize.Fit(size, OutputSize.HdrDownscaleBox);
            bool unchanged = size.Width == decision.SourceWidth && size.Height == decision.SourceHeight;
            return unchanged ? null : new ScaleTarget(size.Width, size.Height);
        }

        int height = decision.OutputHeight > 0 ? decision.OutputHeight : 0;
        if (downscaleForHdr) height = height > 0 ? Math.Min(height, OutputSize.HdrDownscaleBox.Height) : OutputSize.HdrDownscaleBox.Height;
        return height > 0 ? new ScaleTarget(0, height) : null;
    }
}
