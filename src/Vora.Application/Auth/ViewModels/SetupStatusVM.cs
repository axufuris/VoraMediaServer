namespace Vora.Application.Auth.ViewModels;

public class SetupStatusVM
{
    public required bool IsClaimed { get; set; }
    public required int RegistrationMode { get; set; }
    public required string ServerName { get; set; }
    public required bool EmailEnabled { get; set; }
}
