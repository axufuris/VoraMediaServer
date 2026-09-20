using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vora.Application.Serialization;

// Every instant leaves the API marked UTC. A DateTime with Kind=Unspecified
// serializes without a zone by default, and JavaScript reads a zone-less
// timestamp as the viewer's local time — so the same string means a different
// moment on every client.
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    internal const string Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ReadUtc(ref reader);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ToUtc(value).ToString(Format, CultureInfo.InvariantCulture));

    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    internal static DateTime ReadUtc(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String
            && DateTimeOffset.TryParse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var offset))
        {
            return offset.UtcDateTime;
        }

        return ToUtc(reader.GetDateTime());
    }
}

public sealed class NullableUtcDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : UtcDateTimeConverter.ReadUtc(ref reader);

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(UtcDateTimeConverter.ToUtc(value.Value).ToString(UtcDateTimeConverter.Format, CultureInfo.InvariantCulture));
    }
}
