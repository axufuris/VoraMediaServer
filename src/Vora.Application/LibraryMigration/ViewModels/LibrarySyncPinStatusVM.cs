using Vora.Plugins.Dtos;

namespace Vora.Application.LibraryMigration.ViewModels;

public class LibrarySyncPinStatusVM
{
    public required string PinId { get; set; }
    public required LibrarySyncPinStatus Status { get; set; }
    public LibrarySyncTokenVM? Token { get; set; }
}
