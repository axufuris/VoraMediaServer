using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Routing;

namespace Vora.Api.Extensions;

internal static class SwaggerOperationIds
{
    public static string Build(ApiDescription api)
    {
        var explicitName = api.ActionDescriptor.EndpointMetadata
            .OfType<IEndpointNameMetadata>()
            .FirstOrDefault()?.EndpointName;
        if (!string.IsNullOrEmpty(explicitName))
        {
            return explicitName;
        }

        var method = (api.HttpMethod ?? "GET").ToUpperInvariant();
        var template = api.RelativePath ?? string.Empty;

        var nounSegments = new List<string>();
        var paramSegments = new List<string>();

        foreach (var segment in template.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(segment, "api", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (segment.StartsWith('{') && segment.EndsWith('}'))
            {
                var inner = segment[1..^1];
                var name = inner.Split(':')[0];
                paramSegments.Add(name);
            }
            else
            {
                nounSegments.Add(ToPascalCase(segment));
            }
        }

        var verb = method switch
        {
            "GET" => paramSegments.Count > 0 ? "Get" : "List",
            "POST" => "Create",
            "PUT" => "Update",
            "PATCH" => "Patch",
            "DELETE" => "Delete",
            _ => ToPascalCase(method.ToLowerInvariant()),
        };

        var operation = verb + string.Concat(nounSegments);
        if (paramSegments.Count > 0)
        {
            operation += "By" + string.Join("And", paramSegments.Select(ToPascalCase));
        }
        return operation;
    }

    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }
        var parts = input.Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p =>
            p.Length == 1
                ? char.ToUpperInvariant(p[0]).ToString()
                : char.ToUpperInvariant(p[0]) + p[1..]));
    }
}
