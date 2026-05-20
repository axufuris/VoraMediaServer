using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Vora.Application.Actors;
using Vora.Domain.Entities.Actors;

namespace Vora.Infrastructure.Persistence.Repositories;

public class ActorRepository(VoraDbContext context) : IActorRepository
{
    public Task<T?> GetProjectedByIdAsync<T>(Guid id, Expression<Func<Actor, T>> projection) =>
        context.Actors
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(projection)
            .FirstOrDefaultAsync();

    public Task<Actor?> GetActorByIdAsync(Guid id) =>
        context.Actors.FindAsync(id).AsTask();

    public Task<Actor?> GetActorByNameAsync(string name) =>
        context.Actors.FirstOrDefaultAsync(a => a.Name == name);

    public async Task<List<Actor>> GetActorsByTmdbIdsOrNamesAsync(IEnumerable<int> tmdbIds, IEnumerable<string> names) =>
        await context.Actors
            .Where(a => tmdbIds.Contains(a.TmdbId) || names.Contains(a.Name))
            .ToListAsync();

    public async Task<IEnumerable<Guid>> GetActorIdsMissingMetadataAsync(int limit = 50) =>
        await context.Actors
            .Where(a => !a.IsCustom && a.Biography == null)
            .Select(a => a.Id)
            .Take(limit)
            .ToListAsync();

    public async Task UpdateActorAsync(Actor actor)
    {
        if (context.Entry(actor).State == EntityState.Detached)
        {
            context.Actors.Update(actor);
        }
        await context.SaveChangesAsync();
    }

    public Task AddActorsAsync(IEnumerable<Actor> actors) =>
        context.Actors.AddRangeAsync(actors);

    public async Task AddActorAsync(Actor actor)
    {
        await context.Actors.AddAsync(actor);
        await context.SaveChangesAsync();
    }
}
