namespace Vora.Application.Streaming;

public enum HistoryDelivery
{
    DirectPlay,
    DirectStream,
    Transcode
}

public sealed record StreamHistoryQuery(
    int Page,
    int PageSize,
    string Search,
    Guid? ProfileId,
    TimeZoneInfo TimeZone,
    IReadOnlyCollection<Guid> LibraryIds,
    IReadOnlyCollection<HistoryDelivery> Deliveries);
