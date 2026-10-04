using Vora.Domain.Entities.Iptv;

namespace Vora.Application.Iptv.ViewModels;

public class IptvRecordingSessionVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? EpisodeTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? OutputFilePath { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CommercialMarkersJson { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? ExternalProgramId { get; set; }
    public IptvRecordingScheduleVM Schedule { get; set; } = new();

    public static IptvRecordingSessionVM FromEntity(IptvRecordingSession session) => new()
    {
        Id = session.Id,
        Title = session.Title,
        EpisodeTitle = session.EpisodeTitle,
        SeasonNumber = session.SeasonNumber,
        EpisodeNumber = session.EpisodeNumber,
        StartTime = session.StartTime,
        EndTime = session.EndTime,
        Status = session.Status.ToString(),
        OutputFilePath = session.OutputFilePath,
        ErrorMessage = session.ErrorMessage,
        CommercialMarkersJson = session.CommercialMarkersJson,
        FileSizeBytes = session.FileSizeBytes,
        ExternalProgramId = session.ExternalProgramId,
        Schedule = new IptvRecordingScheduleVM
        {
            IsSeries = session.Schedule?.IsSeriesRecording ?? false,
            Channel = new IptvRecordingChannelVM
            {
                Name = session.Schedule?.Channel?.Name ?? "Unknown Channel",
                LogoUrl = session.Schedule?.Channel?.LogoUrl,
            },
        },
    };
}

public class IptvRecordingScheduleVM
{
    public bool IsSeries { get; set; }
    public IptvRecordingChannelVM Channel { get; set; } = new();
}

public class IptvRecordingChannelVM
{
    public string Name { get; set; } = "Unknown Channel";
    public string? LogoUrl { get; set; }
}

public class ScheduleRecordingResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid ChannelId { get; set; }
}

public class DvrPlaybackUrlResponse
{
    public string Url { get; set; } = string.Empty;
}
