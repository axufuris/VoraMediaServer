using System.Linq.Expressions;
using Vora.Domain.Entities.Library;

namespace Vora.Application.Libraries;

public interface ILibraryRepository
{
    Task<T?> GetProjectedByIdAsync<T>(Guid id, Expression<Func<MediaLibrary, T>> projection);
    Task<MediaLibrary?> GetForUpdateAsync(Guid id);
    Task<IEnumerable<T>> GetAllProjectedAsync<T>(Expression<Func<MediaLibrary, T>> projection, bool hasAllAccess = true, List<Guid>? allowedLibs = null);
    Task<Guid> CreateLibraryAsync(MediaLibrary library);
    Task<IEnumerable<MediaLibrary>> GetAllLibrariesAsync();
    Task UpdateLibraryAsync(MediaLibrary library);
    Task CleanUpOrphanedMediaAsync(Guid libraryId);
    Task DeleteLibraryAsync(Guid id, CancellationToken cancellationToken = default);
}