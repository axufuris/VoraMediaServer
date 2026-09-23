namespace Vora.Application.Artwork;

// Uploading an image answers with where it now lives. Music artwork, collection
// artwork and profile images all did that with `new { url }` — three anonymous
// types for one shape, each describing nothing in the OpenAPI document. One
// named class because it is one answer, not three.
public class UploadedImageResponse
{
    public required string Url { get; set; }
}
