namespace Vora.Application.Users.ViewModels;

// Changing an email address does not take effect until the new address is
// confirmed, so the caller has to know whether a verification mail went out —
// the UI says something different in each case.
public class UpdateUserResponse
{
    public bool EmailVerificationSent { get; set; }
}
