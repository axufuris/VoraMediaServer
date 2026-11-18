using System.Text.Json;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Auth.External.Oidc;

public static class OidcClaimReader
{
    public static bool NeedsUserInfo(AuthProvider provider, IReadOnlyDictionary<string, object> idClaims)
    {
        var missingEmail = ReadString(Find(idClaims, provider.EmailClaim)) is null;
        var needsGroups = (!string.IsNullOrWhiteSpace(provider.RequiredGroup) || !string.IsNullOrWhiteSpace(provider.AdminGroup))
            && Find(idClaims, provider.GroupsClaim) is null;
        return missingEmail || needsGroups;
    }

    public static ExternalClaims? Read(AuthProvider provider, IReadOnlyDictionary<string, object> idClaims, JsonElement? userInfo)
    {
        var subject = ReadString(Find(idClaims, "sub"));
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        var info = userInfo is { ValueKind: JsonValueKind.Object } element
            && element.TryGetProperty("sub", out var infoSubject)
            && infoSubject.ValueKind == JsonValueKind.String
            && string.Equals(infoSubject.GetString(), subject, StringComparison.Ordinal)
                ? element
                : (JsonElement?)null;

        object? Lookup(string name) => Find(idClaims, name) ?? (info is { } e && e.TryGetProperty(name, out var value) ? (object)value : null);

        var email = ReadString(Lookup(provider.EmailClaim));
        var verified = email is not null && ReadBool(Lookup("email_verified"));
        var name = ReadString(Lookup(provider.NameClaim)) ?? ReadString(Lookup("preferred_username"));
        var groups = ReadStrings(Lookup(provider.GroupsClaim));

        return new ExternalClaims(subject.Trim(), email?.Trim(), verified, name?.Trim(), groups);
    }

    private static object? Find(IReadOnlyDictionary<string, object> claims, string name) =>
        claims.TryGetValue(name, out var value) ? value : null;

    public static string? ReadString(object? value) => value switch
    {
        string text => string.IsNullOrWhiteSpace(text) ? null : text,
        JsonElement { ValueKind: JsonValueKind.String } element => string.IsNullOrWhiteSpace(element.GetString()) ? null : element.GetString(),
        _ => null
    };

    public static bool ReadBool(object? value) => value switch
    {
        bool flag => flag,
        string text => string.Equals(text, "true", StringComparison.OrdinalIgnoreCase),
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.String } element => string.Equals(element.GetString(), "true", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    public static IReadOnlyCollection<string> ReadStrings(object? value)
    {
        var result = new List<string>();
        switch (value)
        {
            case string text:
                result.AddRange(text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                break;
            case JsonElement { ValueKind: JsonValueKind.String } element:
                result.AddRange((element.GetString() ?? string.Empty).Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                break;
            case JsonElement { ValueKind: JsonValueKind.Array } array:
                result.AddRange(array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString() ?? string.Empty));
                break;
            case IEnumerable<object> items:
                result.AddRange(items.Select(ReadString).OfType<string>());
                break;
        }

        return result.Where(item => item.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
