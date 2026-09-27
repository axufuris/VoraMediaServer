namespace Vora.Domain.Entities.Users;

public class UserProviderConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string ProviderName { get; set; }
    public required string AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

    public Guid UserId { get; set; }
    public virtual User User { get; set; } = null!;
}
