namespace Vora.Domain.Enums;

public enum IptvRecordingSessionStatus
{
    Pending = 0,
    Recording = 1,
    PostProcessing = 2,
    DetectingCommercials = 3,
    Completed = 4,
    Failed = 5,
    Conflict = 6,
    Cancelled = 7
}
