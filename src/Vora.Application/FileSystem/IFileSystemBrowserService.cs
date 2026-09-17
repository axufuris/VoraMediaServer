using Vora.Application.FileSystem.ViewModels;

namespace Vora.Application.FileSystem;

public interface IFileSystemBrowserService
{
    Task<List<FileSystemRootVM>> GetAllowedRootsAsync();
    Task<FileSystemListingVM> ListAsync(string path);
}
