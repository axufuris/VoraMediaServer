namespace Vora.Application.LibraryMigration.ViewModels;

public class LibrarySyncPinVM
{
    public required string PinId { get; set; }
    public required string Code { get; set; }
    public required string VerificationUrl { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
