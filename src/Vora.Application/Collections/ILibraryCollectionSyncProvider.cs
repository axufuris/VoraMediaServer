namespace Vora.Application.Collections;

public interface ILibraryCollectionSyncProvider
{
    Task<IReadOnlyCollection<Guid>> FindInLibraryAsync(LibraryCollectionSyncRequest request, CancellationToken cancellationToken);
}

public sealed record LibraryCollectionSyncRequest(Guid CollectionId, string Theme, Guid? LibraryId);
