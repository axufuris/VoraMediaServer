using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vora.Application.Tasks;

public sealed class TaskRecipe
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly JsonObject _arguments;

    private TaskRecipe(string kind, JsonObject arguments)
    {
        Kind = kind;
        _arguments = arguments;
    }

    public string Kind { get; }

    public static TaskRecipe Of(string kind, object? arguments = null) =>
        new(kind, arguments == null ? new JsonObject() : JsonSerializer.SerializeToNode(arguments, Options) as JsonObject ?? new JsonObject());

    public static TaskRecipe FromJson(string kind, string argumentsJson)
    {
        try
        {
            return new(kind, JsonNode.Parse(argumentsJson) as JsonObject ?? new JsonObject());
        }
        catch (JsonException)
        {
            return new(kind, new JsonObject());
        }
    }

    public string ArgumentsJson => _arguments.ToJsonString();

    public Guid Guid(string name) => OptionalGuid(name) ?? System.Guid.Empty;

    public Guid? OptionalGuid(string name) =>
        _arguments[name] is JsonValue value && value.TryGetValue<string>(out var text) && System.Guid.TryParse(text, out var id) ? id : null;

    public string? Text(string name) =>
        _arguments[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public bool Flag(string name) =>
        _arguments[name] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    public int Number(string name) =>
        _arguments[name] is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;

    public TaskRecipe WithNumber(string name, int number)
    {
        var copy = (JsonObject)_arguments.DeepClone();
        copy[name] = number;
        return new(Kind, copy);
    }
}
