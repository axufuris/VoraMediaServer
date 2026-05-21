namespace Vora.Application.Auth.ViewModels;

public class InvitationVM
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public Guid? InvitedByUserId { get; set; }
}

public class CreateInvitationRequest
{
    public required string Email { get; set; }
    public int? ExpiresInDays { get; set; }
}

public class CreateInvitationResponse
{
    public required InvitationVM Invitation { get; set; }
    public required bool EmailSent { get; set; }
    public string? Message { get; set; }
}

public class ValidateInvitationRequest
{
    public required string Token { get; set; }
}

public class ValidateInvitationResponse
{
    public required string Email { get; set; }
    public DateTime ExpiresAt { get; set; }
}
