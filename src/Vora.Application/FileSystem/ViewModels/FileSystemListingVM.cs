namespace Vora.Application.FileSystem.ViewModels;

public class FileSystemListingVM
{
    public string Path { get; set; } = string.Empty;
    public string? ParentPath { get; set; }
    public List<FileSystemEntryVM> Folders { get; set; } = new();
}

public class FileSystemEntryVM
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool HasChildren { get; set; }
}
