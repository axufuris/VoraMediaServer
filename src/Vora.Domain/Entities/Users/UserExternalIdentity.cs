namespace Vora.Domain.Entities.Users;

public class UserExternalIdentity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid ProviderId { get; set; }
    public required string Subject { get; set; }
    public string? Email { get; set; }
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSignInAt { get; set; }

    public User? User { get; set; }
    public AuthProvider? Provider { get; set; }
}
