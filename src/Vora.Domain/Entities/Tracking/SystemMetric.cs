namespace Vora.Domain.Entities.Tracking;

public class SystemMetric
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int ActiveStreams { get; set; }
    public int ActiveTranscodes { get; set; }
    public double CpuUsagePercentage { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
