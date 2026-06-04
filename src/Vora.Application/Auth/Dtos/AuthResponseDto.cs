namespace Vora.Application.Auth.Dtos;

public class AuthResponseDto
{
    public required string AccessToken { get; set; }
    public required Guid UserId { get; set; }
    public required string DisplayName { get; set; }
    public required bool IsAdmin { get; set; }
}
