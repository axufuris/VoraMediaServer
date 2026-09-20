using System.Linq.Expressions;
using Vora.Domain.Entities.Library;

namespace Vora.Application.Collections.Dtos;

public class CollectionScheduleDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int SyncIntervalDays { get; set; }
    public DateTime? ContentSyncedAt { get; set; }
    public DateTime? ChronologySyncedAt { get; set; }

    public static Expression<Func<Collection, CollectionScheduleDto>> Projection =>
        c => new CollectionScheduleDto
        {
            Id = c.Id,
            Title = c.Title,
            SyncIntervalDays = c.SyncIntervalDays,
            ContentSyncedAt = c.ContentSyncedAt,
            ChronologySyncedAt = c.ChronologySyncedAt
        };
}
