namespace Vora.Application.FileSystem;

public sealed record UploadedFile(Stream Content, string FileName, string? ContentType = null);
