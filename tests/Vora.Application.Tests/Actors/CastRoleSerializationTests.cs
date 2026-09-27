using System.Text.Json;
using System.Text.Json.Serialization;
using Vora.Application.Actors.ViewModels;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Actors;

// MediaCastRole is a [Flags] enum, so JsonStringEnumConverter emits a combined
// value as "Actor, Producer". An OpenAPI enum schema describes a single value,
// so a strictly-typed generated client (Android, kotlinx-serialization) throws
// parsing it. Every VM that carries the enum must keep it off the wire and
// expose the formatted `role` string instead.
//
// This was fixed on CastMemberVM but missed on ActorRoleVM, which crashed the
// Android filmography for anyone credited as both actor and crew — so the rule
// is pinned here rather than left to a code comment.
public class CastRoleSerializationTests
{
    // Mirrors AddVoraJsonOptions in ServiceRegistrationExtensions.
    private static readonly JsonSerializerOptions ApiOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private const MediaCastRole Combined = MediaCastRole.Actor | MediaCastRole.Producer;

    [Fact]
    public void A_combined_role_would_serialize_as_a_comma_joined_string()
    {
        // The behaviour the [JsonIgnore] exists to keep off the wire.
        JsonSerializer.Serialize(Combined, ApiOptions).Should().Be("\"Actor, Producer\"");
    }

    [Fact]
    public void ActorRoleVM_does_not_put_the_flags_enum_on_the_wire()
    {
        var json = JsonSerializer.Serialize(new ActorRoleVM { Title = "The Matrix", Roles = Combined }, ApiOptions);

        json.Should().NotContain("\"roles\"");
        json.Should().Contain("\"role\"");
    }

    [Fact]
    public void CastMemberVM_does_not_put_the_flags_enum_on_the_wire()
    {
        var json = JsonSerializer.Serialize(new CastMemberVM { Name = "Keanu Reeves", Roles = Combined }, ApiOptions);

        json.Should().NotContain("\"roles\"");
        json.Should().Contain("\"role\"");
    }

    [Fact]
    public void ActorRoleVM_still_reports_a_combined_role_as_readable_text()
    {
        new ActorRoleVM { Roles = Combined }.Role.Should().NotBeNullOrWhiteSpace();
    }

    // Guards the whole class of bug rather than the two known instances: any new
    // VM exposing MediaCastRole has to hide it too.
    [Fact]
    public void No_view_model_exposes_a_flags_cast_role_property()
    {
        var offenders = typeof(ActorRoleVM).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Name.EndsWith("VM", StringComparison.Ordinal))
            .SelectMany(t => t.GetProperties().Select(p => (Type: t, Property: p)))
            .Where(x => x.Property.PropertyType == typeof(MediaCastRole))
            .Where(x => x.Property.GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: true).Length == 0)
            .Select(x => $"{x.Type.Name}.{x.Property.Name}")
            .ToList();

        offenders.Should().BeEmpty("a [Flags] cast role must carry [JsonIgnore] and be surfaced through the formatted `Role` string");
    }
}
