namespace Vora.Application.Email;

public interface IEmailTransport
{
    Task SendAsync(QueuedEmail email, CancellationToken cancellationToken);
}
