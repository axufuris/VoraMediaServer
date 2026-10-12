namespace Vora.Application.Streaming;

public sealed record StreamHistoryQuery(int Page, int PageSize, string Search, Guid? ProfileId, TimeZoneInfo TimeZone);
