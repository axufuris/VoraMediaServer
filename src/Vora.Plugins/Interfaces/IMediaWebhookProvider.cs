using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface IMediaWebhookProvider : IVoraPlugin
{
    MediaWebhookParseResult Parse(string body);
}
