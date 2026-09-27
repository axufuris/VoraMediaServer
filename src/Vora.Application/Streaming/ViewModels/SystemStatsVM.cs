namespace Vora.Application.Streaming.ViewModels;

public class SystemStatsVM
{
    public double CpuUsagePercentage { get; set; }
    public double RamUsageGb { get; set; }

    /// <summary>Total bytes on the drive hosting the API's working directory.</summary>
    public long DiskTotalBytes { get; set; }

    /// <summary>Used bytes on that drive (Total - Free).</summary>
    public long DiskUsedBytes { get; set; }

    /// <summary>Free bytes on that drive.</summary>
    public long DiskFreeBytes { get; set; }
}
