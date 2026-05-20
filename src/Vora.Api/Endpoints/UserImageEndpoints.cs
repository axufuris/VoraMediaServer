using Vora.Application.Users;

namespace Vora.Api.Endpoints;

public static class UserImageEndpoints
{
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
        var url = await imgService.UploadAsync(file, oldUrl);

        return Results.Ok(new { Url = url });
    }

    private static IResult ServeCustomImage(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Users", fileName);
        if (!File.Exists(path))
        {
            return Results.NotFound();
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var mime = ext switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

        return Results.File(path, contentType: mime);
    }
}
