using System.Linq.Expressions;
using Vora.Domain.Entities.Library;

namespace Vora.Application.Search.ViewModels;

public class CollectionSearchResultVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }

    public static Expression<Func<Collection, CollectionSearchResultVM>> Projection =>
        c => new CollectionSearchResultVM
        {
            Id = c.Id,
            Title = c.Title,
            PosterUrl = c.PosterUrl
        };
}
