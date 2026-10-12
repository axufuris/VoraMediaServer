using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Vora.Application.Artwork;

namespace Vora.Application.Serialization;

public static class RemoteImageJson
{
    public static void RememberRemoteImages(this JsonSerializerOptions options, IRemoteImageRegistry registry)
    {
        options.TypeInfoResolver = (options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver())
            .WithAddedModifier(typeInfo => RememberOnWrite(typeInfo, registry));
    }

    private static void RememberOnWrite(JsonTypeInfo typeInfo, IRemoteImageRegistry registry)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object) return;

        foreach (var property in typeInfo.Properties)
        {
            var read = property.Get;
            if (read == null || property.PropertyType != typeof(string)) continue;
            if (property.AttributeProvider?.IsDefined(typeof(RemoteImageAttribute), inherit: true) != true) continue;

            property.Get = owner =>
            {
                var value = read(owner);
                registry.Remember(value as string);
                return value;
            };
        }
    }
}
