using System.Linq.Expressions;
using Vora.Domain.Entities.Actors;

namespace Vora.Application.Search.ViewModels;

public class ActorSearchResultVM
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }

    public static Expression<Func<Actor, ActorSearchResultVM>> Projection =>
        a => new ActorSearchResultVM
        {
            Id = a.Id,
            Name = a.Name,
            ProfileImageUrl = a.ProfileImageUrl
        };
}
