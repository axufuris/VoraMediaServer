using System.IO;

namespace Vora.Infrastructure.Transcoding;

// Detects properties of the host environment that affect what FFmpeg
// pipelines are reliable to use. Currently checks:
//
//   IsWsl2:
//     True when the container is running on WSL2 (Docker Desktop on
//     Windows). The NVIDIA Container Toolkit on WSL2 only exposes
//     CUDA + NVENC + NVDEC to the container — Vulkan and OpenCL ICDs
//     don't make it across, regardless of which capabilities are
//     requested in docker-compose. This means GPU-side HDR tonemap
//     (tonemap_opencl, libplacebo) can't initialize a device, and the
//     tonemap falls back to CPU. The settings layer reads this flag
//     to decide what HDR tonemap pipeline to default to when the
//     admin leaves HdrTonemapQuality + HdrTranscodeDownscale at
//     "Auto".
//
//   HasNvidiaVulkanIcd:
//     Whether libGLX_nvidia.so.0 (the NVIDIA Vulkan driver) is
//     actually present in the container's loadable library paths.
//     Used as a secondary signal — even on native Linux, if the
//     graphics capability wasn't requested, Vulkan won't work.
//
// All detection runs once and caches. Cheap.
public static class HostEnvironmentDetector
{
    private static readonly object _lock = new();
    private static bool _initialised;
    private static bool _isWsl2;
    private static bool _hasNvidiaVulkanIcd;
    private static bool _hasJellyfinFfmpeg;

    public static bool IsWsl2
    {
        get { EnsureInitialised(); return _isWsl2; }
    }

    public static bool HasNvidiaVulkanIcd
    {
        get { EnsureInitialised(); return _hasNvidiaVulkanIcd; }
    }

    // True when jellyfin-ffmpeg7 (or newer) is installed in the
    // container. Their build ships --enable-cuda-llvm and the
    // tonemap_cuda filter, which is the only HDR tonemap path that
    // works across ALL of our supported container hosts: bare-metal
    // Linux/Unraid AND WSL2/Docker Desktop on Windows. CUDA is the
    // one driver the WSL2 NVIDIA container toolkit passes through.
    public static bool HasJellyfinFfmpeg
    {
        get { EnsureInitialised(); return _hasJellyfinFfmpeg; }
    }

    // Whether the host can plausibly run a GPU HDR tonemap. With
    // jellyfin-ffmpeg installed the answer is yes everywhere CUDA
    // works (tonemap_cuda has no Vulkan/OpenCL requirement). Without
    // it we fall back to looking for the Vulkan ICD on bare-metal
    // Linux as the legacy libplacebo path.
    public static bool CanUseGpuHdrTonemap =>
        HasJellyfinFfmpeg || (!IsWsl2 && HasNvidiaVulkanIcd);

    private static void EnsureInitialised()
    {
        if (_initialised) return;
        lock (_lock)
        {
            if (_initialised) return;
            _isWsl2 = DetectWsl2();
            _hasNvidiaVulkanIcd = DetectNvidiaVulkanIcd();
            _hasJellyfinFfmpeg = DetectJellyfinFfmpeg();
            _initialised = true;
        }
    }

    private static bool DetectJellyfinFfmpeg()
    {
        try
        {
            // The jellyfin-ffmpeg7 apt package installs into
            // /usr/lib/jellyfin-ffmpeg/. We don't probe the binary's
            // help output (slow at startup), just check the dir exists.
            return Directory.Exists("/usr/lib/jellyfin-ffmpeg")
                && File.Exists("/usr/lib/jellyfin-ffmpeg/ffmpeg");
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectWsl2()
    {
        try
        {
            if (!File.Exists("/proc/version")) return false;
            var content = File.ReadAllText("/proc/version");
            return content.Contains("microsoft", System.StringComparison.OrdinalIgnoreCase)
                || content.Contains("WSL", System.StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectNvidiaVulkanIcd()
    {
        // The NVIDIA Vulkan driver is exposed as libGLX_nvidia.so.0 by
        // the Container Toolkit's `graphics` capability. We check the
        // canonical library paths inside the container. If it isn't
        // mounted (typical on WSL2 + Docker Desktop), Vulkan won't
        // find a device even with the loader and ICD JSON installed.
        string[] candidates =
        {
            "/usr/lib/x86_64-linux-gnu/libGLX_nvidia.so.0",
            "/usr/lib64/libGLX_nvidia.so.0",
            "/usr/lib/wsl/lib/libGLX_nvidia.so.0",
        };
        foreach (var path in candidates)
        {
            try
            {
                if (File.Exists(path)) return true;
            }
            catch
            {
                // ignore IO error and move on
            }
        }
        return false;
    }
}
