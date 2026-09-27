using System.Text.Json;
using Vora.Application.Serialization;

namespace Vora.Application.Tests.Serialization;

public class UtcDateTimeConverterTests
{
    private static readonly JsonSerializerOptions Options = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new UtcDateTimeConverter());
        options.Converters.Add(new NullableUtcDateTimeConverter());
        return options;
    }

    private sealed class Payload
    {
        public DateTime At { get; set; }
        public DateTime? MaybeAt { get; set; }
    }

    [Fact]
    public void An_unspecified_instant_goes_out_marked_utc()
    {
        var json = JsonSerializer.Serialize(new Payload { At = new DateTime(2026, 9, 18, 20, 30, 0, DateTimeKind.Unspecified) }, Options);

        json.Should().Contain("\"at\":\"2026-09-18T20:30:00.0000000Z\"");
    }

    [Fact]
    public void A_local_instant_is_converted_rather_than_relabelled()
    {
        var local = new DateTime(2026, 9, 18, 20, 30, 0, DateTimeKind.Local);

        var json = JsonSerializer.Serialize(new Payload { At = local }, Options);

        json.Should().Contain(local.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffff") + "Z");
    }

    [Fact]
    public void A_utc_instant_is_left_alone()
    {
        var json = JsonSerializer.Serialize(new Payload { At = new DateTime(2026, 9, 18, 20, 30, 0, DateTimeKind.Utc) }, Options);

        json.Should().Contain("\"at\":\"2026-09-18T20:30:00.0000000Z\"");
    }

    [Fact]
    public void A_null_instant_stays_null()
    {
        var json = JsonSerializer.Serialize(new Payload { At = DateTime.UnixEpoch, MaybeAt = null }, Options);

        json.Should().Contain("\"maybeAt\":null");
    }

    [Theory]
    [InlineData("2026-09-18T20:30:00Z", "2026-09-18T20:30:00")]
    [InlineData("2026-09-18T20:30:00", "2026-09-18T20:30:00")]
    [InlineData("2026-09-18T22:30:00+02:00", "2026-09-18T20:30:00")]
    [InlineData("2026-09-18T15:30:00-05:00", "2026-09-18T20:30:00")]
    public void Every_inbound_shape_reads_as_the_same_utc_instant(string wire, string expectedUtc)
    {
        var payload = JsonSerializer.Deserialize<Payload>($"{{\"at\":\"{wire}\",\"maybeAt\":\"{wire}\"}}", Options);

        var expected = DateTime.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
        payload!.At.Should().Be(DateTime.SpecifyKind(expected, DateTimeKind.Utc));
        payload.At.Kind.Should().Be(DateTimeKind.Utc);
        payload.MaybeAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void A_round_trip_keeps_the_instant()
    {
        var original = new Payload { At = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), MaybeAt = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Unspecified) };

        var restored = JsonSerializer.Deserialize<Payload>(JsonSerializer.Serialize(original, Options), Options);

        restored!.At.Should().Be(original.At);
        restored.MaybeAt.Should().Be(DateTime.SpecifyKind(original.MaybeAt!.Value, DateTimeKind.Utc));
    }
}
