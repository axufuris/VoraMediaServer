namespace Vora.Application.Email;

public class RenderedEmail
{
    public required string Subject { get; init; }
    public required string HtmlBody { get; init; }
    public required string TextBody { get; init; }
}
