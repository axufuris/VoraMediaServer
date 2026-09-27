namespace Vora.Domain.Entities.Users;

public class AuthRefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FamilyId { get; set; }
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public required string SecurityStamp { get; set; }
    public string? DeviceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public User? User { get; set; }
}
