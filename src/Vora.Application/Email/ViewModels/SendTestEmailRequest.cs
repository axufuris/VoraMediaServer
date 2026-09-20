namespace Vora.Application.Email.ViewModels;

public class SendTestEmailRequest
{
    public string ToAddress { get; set; } = string.Empty;
}

public class SendTestEmailResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}
