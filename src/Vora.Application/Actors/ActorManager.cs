using Microsoft.Extensions.Logging;
using Vora.Application.Actors.ViewModels;
using Vora.Domain.Entities.Actors;

namespace Vora.Application.Actors;

public interface IActorManager
{
    Task<ActorVM?> GetActorProfileAsync(Guid id);
    Task<Guid> CreateCustomActorAsync(string name, string? profileImageUrl);
}

public class ActorManager(IActorRepository repository, ILogger<ActorManager> logger) : IActorManager
{
    public Task<ActorVM?> GetActorProfileAsync(Guid id) =>
        repository.GetProjectedByIdAsync(id, ActorVM.Projection);

    public async Task<Guid> CreateCustomActorAsync(string name, string? profileImageUrl)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Actor name is required.", nameof(name));
        }

        try
        {
            var existing = await repository.GetActorByNameAsync(name);
            if (existing != null)
            {
                return existing.Id;
            }

            var newActor = new Actor
            {
                Name = name,
                ProfileImageUrl = profileImageUrl,
                IsCustom = true
            };

            await repository.AddActorAsync(newActor);
            return newActor.Id;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create custom actor {ActorName}", name);
            throw;
        }
    }
}
