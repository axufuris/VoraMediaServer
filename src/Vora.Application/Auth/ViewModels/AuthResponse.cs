namespace Vora.Application.Auth.ViewModels;

// These two exist only because the original endpoints returned anonymous
// objects (`new { Token = ... }` and `new { Code = ... }`) — there's no
// pre-existing typed shape to point .Produces<>() at, and the wire format
// has to be preserved for the web client. AuthResponseDto already covers
// login/register/claim, so those endpoints reuse it directly instead.

public class ExchangeProfileTokenResponse
{
    public required string Token { get; set; }
}

public class GenerateInviteCodeResponse
{
    public required string Code { get; set; }
}
