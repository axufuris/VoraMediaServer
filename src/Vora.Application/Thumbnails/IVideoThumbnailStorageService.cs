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
}
