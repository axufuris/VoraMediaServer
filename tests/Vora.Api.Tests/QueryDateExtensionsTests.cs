using Vora.Api.Extensions;

namespace Vora.Api.Tests;

public class QueryDateExtensionsTests
{
    [Fact]
    public void A_zoneless_date_is_taken_as_utc()
    {
        var bound = new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Unspecified);

        var normalized = bound.AsUtc();

        normalized.Kind.Should().Be(DateTimeKind.Utc);
        normalized.Should().Be(new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void A_utc_date_is_left_exactly_as_it_is()
    {
        var bound = new DateTime(2026, 9, 6, 13, 45, 0, DateTimeKind.Utc);

        bound.AsUtc().Should().Be(bound);
        bound.AsUtc().Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void A_local_date_is_converted_rather_than_relabelled()
    {
        var local = new DateTime(2026, 9, 6, 13, 45, 0, DateTimeKind.Local);

        var normalized = local.AsUtc();

        normalized.Kind.Should().Be(DateTimeKind.Utc);
        normalized.Should().Be(local.ToUniversalTime());
    }

    [Fact]
    public void The_instant_a_local_date_names_is_preserved()
    {
        var local = new DateTime(2026, 9, 6, 13, 45, 0, DateTimeKind.Local);

        local.AsUtc().Should().Be(local.ToUniversalTime());
    }

    [Fact]
    public void A_missing_optional_date_stays_missing()
    {
        DateTime? absent = null;

        absent.AsUtc().Should().BeNull();
    }

    [Fact]
    public void A_present_optional_date_is_normalized()
    {
        DateTime? bound = new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Unspecified);

        bound.AsUtc()!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Normalizing_twice_changes_nothing()
    {
        var bound = new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Unspecified);

        bound.AsUtc().AsUtc().Should().Be(bound.AsUtc());
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    public void Every_kind_comes_out_writable_to_a_timestamptz_column(DateTimeKind kind)
    {
        var bound = new DateTime(2026, 9, 6, 0, 0, 0, kind);

        bound.AsUtc().Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void The_minimum_date_is_handled_without_overflowing()
    {
        DateTime.MinValue.AsUtc().Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void The_maximum_date_is_handled_without_overflowing()
    {
        DateTime.MaxValue.AsUtc().Kind.Should().Be(DateTimeKind.Utc);
    }
}
