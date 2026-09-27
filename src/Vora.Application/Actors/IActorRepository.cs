using System.Linq.Expressions;
using Vora.Domain.Entities.Actors;

namespace Vora.Application.Actors;

public interface IActorRepository
{
    Task<T?> GetProjectedByIdAsync<T>(Guid id, Expression<Func<Actor, T>> projection);
    Task<Actor?> GetActorByNameAsync(string name);
    Task<List<Actor>> GetActorsByTmdbIdsOrNamesAsync(IEnumerable<int> tmdbIds, IEnumerable<string> names);
    Task<Actor?> GetActorByIdAsync(Guid id);
    Task<IEnumerable<Guid>> GetActorIdsMissingMetadataAsync(int limit = 50);
    Task UpdateActorAsync(Actor actor);
    Task AddActorsAsync(IEnumerable<Actor> actors);
    Task AddActorAsync(Actor actor);
}