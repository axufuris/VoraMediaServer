using System.Text.RegularExpressions;
using Vora.Application.FileSystem;
using Vora.Application.Users;

namespace Vora.Api.Endpoints;

public static partial class UserImageEndpoints
{
    [GeneratedRegex(@"^profile_[A-Za-z0-9._-]+\.(png|jpg|jpeg|webp)$", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileImageFileNameRegex();

    public static RouteGroupBuilder MapUserImageEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/users/images").WithTags("User Images");

        group.MapPost("/upload", UploadAsync)
            .DisableAntiforgery()
            .RequireAuthorization();

        group.MapGet("/custom/{fileName}", ServeCustomImage)
            .AllowAnonymous();

        return group;
    }

    private static async Task<IResult> UploadAsync(HttpRequest request, IUserProfileImageService imgService)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest("Unsupported media type");
        }

        var file = request.Form.Files.FirstOrDefault();
        if (file == null)
        {
            return Results.BadRequest("No file uploaded");
        }

        var oldUrl = request.Form["oldUrl"].ToString();
        await using var stream = file.OpenReadStream();
        var url = await imgService.UploadAsync(new UploadedFile(stream, file.FileName, file.ContentType), oldUrl);

        return Results.Ok(new { Url = url });
    }

    private static IResult ServeCustomImage(string fileName, IUserProfileImageService imgService, HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !ProfileImageFileNameRegex().IsMatch(fileName))
        {
            return Results.NotFound();
        }

        var path = imgService.ResolvePath(fileName);
        if (path == null || !File.Exists(path))
        {
            return Results.NotFound();
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var mime = ext switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

        httpContext.Response.Headers.CacheControl = "public, max-age=2592000, immutable";
        return Results.File(path, contentType: mime);
    }
}
