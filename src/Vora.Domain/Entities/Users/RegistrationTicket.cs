namespace Vora.Domain.Entities.Users;

public class RegistrationTicket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string SecretCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}
