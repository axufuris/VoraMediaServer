using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Vora.Application.Users;

public interface IUserProfileImageService
{
    Task<string> UploadAsync(IFormFile file, string? oldImageUrl);
    void DeleteImage(string? imageUrl);
}

public class UserProfileImageService : IUserProfileImageService
{
    private const string CustomImageUrlPrefix = "/api/users/images/custom/";

    private readonly ILogger<UserProfileImageService> _logger;
    private readonly string _basePath;

    public UserProfileImageService(ILogger<UserProfileImageService> logger)
    {
        _logger = logger;
        _basePath = Path.Combine(AppContext.BaseDirectory, "Users");
        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
        }
    }

    public async Task<string> UploadAsync(IFormFile file, string? oldImageUrl)
    {
        if (!string.IsNullOrWhiteSpace(oldImageUrl))
        {
            DeleteImage(oldImageUrl);
        }

        var ext = Path.GetExtension(file.FileName);
        var fileName = $"profile_{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(_basePath, fileName);

        try
        {
            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);
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
            var physicalPath = Path.Combine(_basePath, fileName);
            if (File.Exists(physicalPath))
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
