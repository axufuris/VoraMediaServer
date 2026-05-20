namespace Vora.Application.Iptv.ViewModels;

public class IptvEpgDiagnosticsVM
{
    public List<DbChannelSample> DbSampleIds { get; set; } = new();
    public List<EpgSourceDiagnostics> Sources { get; set; } = new();
    public ChannelCoverageSummary Coverage { get; set; } = new();
}

public class ChannelCoverageSummary
{
    public int TotalChannels { get; set; }
    public int ChannelsWithEpg { get; set; }
    public double CoverageRate { get; set; }
    public List<DbChannelSample> UncoveredSamples { get; set; } = new();
}

public class DbChannelSample
{
    public string ExternalChannelId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PlaylistName { get; set; } = string.Empty;
}

public class EpgSourceDiagnostics
{
    public Guid SourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string XmlTvUrl { get; set; } = string.Empty;
    public int TotalProgrammes { get; set; }
    public int MatchedProgrammes { get; set; }
    public int MatchedChannels { get; set; }
    public double MatchRate { get; set; }
    public List<string> UnmatchedSamples { get; set; } = new();
    public DateTime? SyncedAt { get; set; }
    public string? LastError { get; set; }
}
