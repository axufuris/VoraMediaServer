using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Providers.Tvdb;

namespace Vora.Application.Tests.Metadata;

public class TvdbCastRoleTests
{
    [Theory]
    [InlineData("Actor", CastRole.Actor)]
    [InlineData("Guest Star", CastRole.Actor)]
    [InlineData("Host", CastRole.Actor)]
    [InlineData("Musical Guest", CastRole.Actor)]
    [InlineData(null, CastRole.Actor)]
    [InlineData("Director", CastRole.Director)]
    [InlineData("Writer", CastRole.Writer)]
    [InlineData("Producer", CastRole.Producer)]
    [InlineData("Executive Producer", CastRole.Producer)]
    [InlineData("Showrunner", CastRole.Producer)]
    [InlineData("Creator", CastRole.Creator)]
    [InlineData("Crew", CastRole.None)]
    public void Each_tvdb_people_type_has_its_role(string? peopleType, CastRole role)
    {
        TvdbMetadataProvider.MapPeopleTypeToRole(peopleType).Should().Be(role);
    }

    [Fact]
    public void Crew_is_left_out_of_the_cast_and_executive_producers_are_not_credited_as_actors()
    {
        var series = JsonDocument.Parse("""
            {
              "name": "Breaking Bad",
              "characters": [
                { "peopleId": 1, "personName": "Bryan Cranston", "name": "Walter White", "peopleType": "Actor", "personImgURL": "https://artworks.thetvdb.com/cranston.jpg" },
                { "peopleId": 2, "personName": "Vince Gilligan", "name": null, "peopleType": "Creator" },
                { "peopleId": 3, "personName": "Mark Johnson", "name": null, "peopleType": "Executive Producer" },
                { "peopleId": 4, "personName": "Pat Grip", "name": "Key Grip", "peopleType": "Crew" }
              ]
            }
            """).RootElement;

        TvdbMetadataProvider.CastOf(series).Select(person => (person.Name, person.TvdbId, person.Roles)).Should().Equal(
            ("Bryan Cranston", 1, CastRole.Actor),
            ("Vince Gilligan", 2, CastRole.Creator),
            ("Mark Johnson", 3, CastRole.Producer));
    }
}
