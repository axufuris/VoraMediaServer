using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vora.Application.FileSystem;
using Vora.Application.Settings;

namespace Vora.Application.Users;

public interface IUserProfileImageService
{
    Task<string> UploadAsync(UploadedFile file, string? oldImageUrl);
    void DeleteImage(string? imageUrl);
    string? ResolvePath(string fileName);
}

public class UserProfileImageService : IUserProfileImageService
{
    private const string CustomImageUrlPrefix = "/api/users/images/custom/";

    private readonly ILogger<UserProfileImageService> _logger;
    private readonly string _basePath;

    public UserProfileImageService(IOptions<StoragePathsOptions> storagePaths, ILogger<UserProfileImageService> logger)
    {
        _logger = logger;
        var configured = storagePaths.Value.UserImages;
        _basePath = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "Users");
        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
        }
    }

    public string? ResolvePath(string fileName) => SafePathResolver.ResolveContainedFilePath(_basePath, fileName);

    public async Task<string> UploadAsync(UploadedFile file, string? oldImageUrl)
    {
        if (!string.IsNullOrWhiteSpace(oldImageUrl))
        {
            DeleteImage(oldImageUrl);
        }

        byte[] imageBytes;
        await using (var buffer = new MemoryStream())
        {
            await file.Content.CopyToAsync(buffer);
            imageBytes = buffer.ToArray();
        }

        var ext = ImageContentValidator.DetectImageExtension(imageBytes);
        if (ext == null)
        {
            throw new InvalidOperationException("Uploaded file is not a supported image (JPEG, PNG, WebP, or GIF).");
        }

        var fileName = $"profile_{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(_basePath, fileName);

        try
        {
            await File.WriteAllBytesAsync(filePath, imageBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload profile image to {FilePath}.", filePath);
            throw;
        }

        return $"{CustomImageUrlPrefix}{fileName}";
    }

    public void DeleteImage(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl) || !imageUrl.StartsWith(CustomImageUrlPrefix))
        {
            return;
        }

        try
        {
            var fileName = imageUrl.Split('/').Last();
            var physicalPath = SafePathResolver.ResolveContainedFilePath(_basePath, fileName);
            if (physicalPath != null && File.Exists(physicalPath))
            {
                File.Delete(physicalPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete profile image at {ImageUrl}.", imageUrl);
        }
    }
}
