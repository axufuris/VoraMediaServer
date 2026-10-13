using System.Net;
using System.Text;
using System.Text.Json;
using Vora.Application.Ai;

namespace Vora.Application.Tests.Ai;

internal sealed class StubAiServer : HttpMessageHandler, IHttpClientFactory
{
    public sealed record Call(HttpMethod Method, string Url, string? Authorization, JsonElement Body)
    {
        public bool Has(string property) => Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty(property, out _);
        public string Path => new Uri(Url).AbsolutePath;
    }

    private readonly Func<Call, int, HttpResponseMessage> _respond;

    public StubAiServer(Func<Call, int, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    public StubAiServer(Func<Call, HttpResponseMessage> respond) : this((call, _) => respond(call))
    {
    }

    public List<Call> Calls { get; } = new();

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var text = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var body = string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        var call = new Call(request.Method, request.RequestUri?.ToString() ?? string.Empty, request.Headers.Authorization?.ToString(), body);
        Calls.Add(call);
        return _respond(call, Calls.Count);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Chat(string content, int promptTokens = 12, int completionTokens = 8) =>
        Json(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { role = "assistant", content } } },
            usage = new { prompt_tokens = promptTokens, completion_tokens = completionTokens, total_tokens = promptTokens + completionTokens }
        }));

    public static HttpResponseMessage Embeddings(params (int? Index, float[] Vector)[] entries) =>
        Json(JsonSerializer.Serialize(new
        {
            data = entries.Select(e => e.Index is int index
                ? (object)new { index, embedding = e.Vector }
                : new { embedding = e.Vector }),
            usage = new { prompt_tokens = 10, total_tokens = 10 }
        }));

    public static HttpResponseMessage Models(params string[] ids) =>
        Json(JsonSerializer.Serialize(new { data = ids.Select(id => new { id }) }));

    public static AiConnection OpenAi(params (string Key, string Value)[] more) =>
        Connection(more.Append((AiConnection.ApiKeyKey, "sk-example")).ToArray());

    public static AiConnection Ollama(params (string Key, string Value)[] more) =>
        Connection(more.Append((AiConnection.BaseUrlKey, "http://ollama.example:11434/v1")).ToArray());

    public static AiConnection Connection(params (string Key, string Value)[] values) =>
        new(Settings(values));

    public static Dictionary<string, string> Settings(params (string Key, string Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value);
}
