using System.Linq.Expressions;
using Vora.Domain.Entities.Library;

namespace Vora.Application.Collections.Dtos;

public class CollectionScheduleDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;

    public static Expression<Func<Collection, CollectionScheduleDto>> Projection =>
        c => new CollectionScheduleDto
        {
            Id = c.Id,
            Title = c.Title
        };
}
