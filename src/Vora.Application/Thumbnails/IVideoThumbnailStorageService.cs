namespace Vora.Application.Thumbnails;

public interface IVideoThumbnailStorageService
{
    string RootDirectory { get; }
    string GetItemDirectory(Guid mediaItemId);
    string GetSpritePath(Guid mediaItemId);
    string GetVttPath(Guid mediaItemId);
    string GetVersionMarkerPath(Guid mediaItemId);
    void EnsureItemDirectory(Guid mediaItemId);
    void DeleteItemDirectory(Guid mediaItemId);
    bool HasGeneratedAssets(Guid mediaItemId);

    // Per-cut sprite/vtt, stored under the owning ("source") part's id inside the
    // item directory, so an item can hold one sprite set per distinct runtime.
    string GetPartSpritePath(Guid mediaItemId, Guid sourcePartId);
    string GetPartVttPath(Guid mediaItemId, Guid sourcePartId);
    void EnsurePartDirectory(Guid mediaItemId, Guid sourcePartId);
    bool HasPartAssets(Guid mediaItemId, Guid sourcePartId);
}
