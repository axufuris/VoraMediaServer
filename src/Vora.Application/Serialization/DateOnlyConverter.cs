using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vora.Application.Serialization;

// A calendar date on the wire: "2026-01-01", never an instant. Reading is
// deliberately forgiving — a client that still sends the old
// "2026-01-01T00:00:00Z" shape keeps working, and its date is taken as written
// rather than shifted into the server's timezone.
public sealed class DateOnlyConverter : JsonConverter<DateOnly>
{
    internal const string Format = "yyyy-MM-dd";

    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ReadDate(ref reader);

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));

    internal static DateOnly ReadDate(ref Utf8JsonReader reader)
    {
        var raw = reader.GetString();
        if (string.IsNullOrWhiteSpace(raw)) return default;

        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var offset))
        {
            return DateOnly.FromDateTime(offset.UtcDateTime);
        }

        throw new JsonException($"Could not read '{raw}' as a date.");
    }
}

public sealed class NullableDateOnlyConverter : JsonConverter<DateOnly?>
{
    public override DateOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : DateOnlyConverter.ReadDate(ref reader);

    public override void Write(Utf8JsonWriter writer, DateOnly? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.ToString(DateOnlyConverter.Format, CultureInfo.InvariantCulture));
    }
}
