using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Providers.Tmdb;

namespace Vora.Application.Tests.Metadata;

public class TmdbCrewRoleTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private static List<(string Name, CastRole Roles)> Credits(MetadataResult result) =>
        result.Cast.Select(person => (person.Name, person.Roles)).ToList();

    [Fact]
    public void A_movies_crew_is_credited_by_job_so_assistant_directors_casting_and_story_editors_are_left_out()
    {
        var movie = Json("""
            {
              "id": 361743, "title": "Top Gun: Maverick",
              "credits": {
                "cast": [
                  { "id": 500, "name": "Tom Cruise", "character": "Capt. Pete 'Maverick' Mitchell", "profile_path": "/cruise.jpg" },
                  { "id": 69597, "name": "Miles Teller", "character": "Lt. Bradley 'Rooster' Bradshaw" }
                ],
                "crew": [
                  { "id": 1580131, "name": "Scott Robertson", "department": "Directing", "job": "First Assistant Director" },
                  { "id": 1338372, "name": "Andrew Stahl", "department": "Directing", "job": "Second Unit Director" },
                  { "id": 1404244, "name": "Robert E. Kay", "department": "Directing", "job": "Script Supervisor" },
                  { "id": 1408290, "name": "Denise Chamian", "department": "Production", "job": "Casting" },
                  { "id": 1399071, "name": "Tommy Harper", "department": "Production", "job": "Unit Production Manager" },
                  { "id": 1393558, "name": "Ann Lou", "department": "Production", "job": "Location Manager" },
                  { "id": 1530, "name": "Jerry Bruckheimer", "department": "Production", "job": "Producer" },
                  { "id": 500, "name": "Tom Cruise", "department": "Production", "job": "Producer" },
                  { "id": 113194, "name": "David Ellison", "department": "Production", "job": "Executive Producer" },
                  { "id": 9033, "name": "Ehren Kruger", "department": "Writing", "job": "Screenplay" },
                  { "id": 77777, "name": "Pat Notes", "department": "Writing", "job": "Story Editor" },
                  { "id": 77778, "name": "Sam Helper", "department": "Writing", "job": "Writers' Assistant" },
                  { "id": 1376, "name": "Jim Cash", "department": "Writing", "job": "Characters" },
                  { "id": 1356425, "name": "Peter Craig", "department": "Writing", "job": "Story" },
                  { "id": 107113, "name": "Joseph Kosinski", "department": "Directing", "job": "Director", "profile_path": "/kosinski.jpg" }
                ]
              }
            }
            """);

        var result = TmdbMetadataProvider.MapResult(movie, isTv: false);

        Credits(result).Should().Equal(
            ("Tom Cruise", CastRole.Actor | CastRole.Producer),
            ("Miles Teller", CastRole.Actor),
            ("Joseph Kosinski", CastRole.Director),
            ("Ehren Kruger", CastRole.Writer),
            ("Peter Craig", CastRole.Writer),
            ("Jim Cash", CastRole.Writer),
            ("Jerry Bruckheimer", CastRole.Producer),
            ("David Ellison", CastRole.Producer));
        result.Cast.Single(person => person.Name == "Joseph Kosinski").Should().BeEquivalentTo(new
        {
            TmdbId = 107113,
            ProfileImageUrl = "https://image.tmdb.org/t/p/w185/kosinski.jpg",
            CharacterName = (string?)null
        });
        result.Cast.Single(person => person.Name == "Tom Cruise").CharacterName.Should().Be("Capt. Pete 'Maverick' Mitchell");
    }

    [Fact]
    public void A_co_director_is_a_director_listed_after_the_director()
    {
        var movie = Json("""
            {
              "id": 14160, "title": "Up",
              "credits": {
                "cast": [ { "id": 2, "name": "Ed Asner", "character": "Carl Fredricksen" } ],
                "crew": [
                  { "id": 11, "name": "Bob Peterson", "department": "Directing", "job": "Co-Director" },
                  { "id": 12, "name": "Kim Lee", "department": "Directing", "job": "Assistant Director" },
                  { "id": 10, "name": "Pete Docter", "department": "Directing", "job": "Director" },
                  { "id": 11, "name": "Bob Peterson", "department": "Writing", "job": "Screenplay" }
                ]
              }
            }
            """);

        var result = TmdbMetadataProvider.MapResult(movie, isTv: false);

        Credits(result).Should().Equal(
            ("Ed Asner", CastRole.Actor),
            ("Pete Docter", CastRole.Director),
            ("Bob Peterson", CastRole.Director | CastRole.Writer));
    }

    [Fact]
    public void A_shows_creators_come_from_created_by_and_its_crew_is_read_from_a_jobs_list_too()
    {
        var show = Json("""
            {
              "id": 1396, "name": "Breaking Bad",
              "created_by": [ { "id": 66633, "name": "Vince Gilligan", "profile_path": "/gilligan.jpg" } ],
              "credits": {
                "cast": [ { "id": 17419, "name": "Bryan Cranston", "character": "Walter White" } ],
                "crew": [
                  { "id": 24951, "name": "Mark Johnson", "jobs": [ { "credit_id": "a", "job": "Executive Producer", "episode_count": 62 } ], "department": "Production" },
                  { "id": 66633, "name": "Vince Gilligan", "jobs": [ { "credit_id": "b", "job": "Executive Producer" }, { "credit_id": "c", "job": "Writer" } ], "department": "Production" },
                  { "id": 1223197, "name": "Moira Walley-Beckett", "jobs": [ { "credit_id": "d", "job": "Story Editor" }, { "credit_id": "e", "job": "Staff Writer" } ], "department": "Writing" },
                  { "id": 1280071, "name": "Sharon Bialy", "job": "Casting", "department": "Production" },
                  { "id": 1222211, "name": "Peter Gould", "job": "Co-Executive Producer", "department": "Production" }
                ]
              }
            }
            """);

        var result = TmdbMetadataProvider.MapResult(show, isTv: true);

        Credits(result).Should().Equal(
            ("Bryan Cranston", CastRole.Actor),
            ("Vince Gilligan", CastRole.Creator | CastRole.Producer | CastRole.Writer),
            ("Mark Johnson", CastRole.Producer),
            ("Peter Gould", CastRole.Producer));
    }

    [Fact]
    public void An_episode_keeps_its_guest_stars_directors_and_writers_and_nothing_else()
    {
        var episode = Json("""
            {
              "id": 62085, "name": "Ozymandias", "episode_number": 14, "runtime": null, "still_path": "/still.jpg",
              "guest_stars": [ { "id": 1, "name": "Steven Michael Quezada", "character": "Steven Gomez" } ],
              "crew": [
                { "id": 2, "name": "Rian Johnson", "department": "Directing", "job": "Director" },
                { "id": 3, "name": "Ann Assistant", "department": "Directing", "job": "First Assistant Director" },
                { "id": 4, "name": "Sam Supervisor", "department": "Directing", "job": "Script Supervisor" },
                { "id": 5, "name": "Moira Walley-Beckett", "department": "Writing", "job": "Teleplay" },
                { "id": 6, "name": "Ed Editor", "department": "Writing", "job": "Executive Story Editor" },
                { "id": 7, "name": "Pat Producer", "department": "Production", "job": "Executive Producer" },
                { "id": 8, "name": "Kim Casting", "department": "Production", "job": "Casting" },
                { "id": 9, "name": "Michael Slovis", "department": "Camera", "job": "Director of Photography" }
              ]
            }
            """);

        var result = TmdbMetadataProvider.MapEpisode(episode);

        Credits(result).Should().Equal(
            ("Steven Michael Quezada", CastRole.Actor),
            ("Rian Johnson", CastRole.Director),
            ("Moira Walley-Beckett", CastRole.Writer));
        result.RuntimeMinutes.Should().BeNull();
        result.PosterUrl.Should().Be("https://image.tmdb.org/t/p/w500/still.jpg");
    }

    [Theory]
    [InlineData("Director", CastRole.Director)]
    [InlineData("Co-Director", CastRole.Director)]
    [InlineData("Series Director", CastRole.Director)]
    [InlineData("Screenplay", CastRole.Writer)]
    [InlineData("Writer", CastRole.Writer)]
    [InlineData("Story", CastRole.Writer)]
    [InlineData("Teleplay", CastRole.Writer)]
    [InlineData("Novel", CastRole.Writer)]
    [InlineData("Characters", CastRole.Writer)]
    [InlineData("Author", CastRole.Writer)]
    [InlineData("Book", CastRole.Writer)]
    [InlineData("Original Story", CastRole.Writer)]
    [InlineData("Comic Book", CastRole.Writer)]
    [InlineData("Producer", CastRole.Producer)]
    [InlineData("Executive Producer", CastRole.Producer)]
    [InlineData("Co-Producer", CastRole.Producer)]
    [InlineData("Co-Executive Producer", CastRole.Producer)]
    [InlineData("Associate Producer", CastRole.Producer)]
    [InlineData("Supervising Producer", CastRole.Producer)]
    [InlineData("Consulting Producer", CastRole.Producer)]
    [InlineData("Line Producer", CastRole.Producer)]
    [InlineData("Creator", CastRole.Creator)]
    [InlineData("First Assistant Director", CastRole.None)]
    [InlineData("Second Assistant Director", CastRole.None)]
    [InlineData("Assistant Director", CastRole.None)]
    [InlineData("Second Unit Director", CastRole.None)]
    [InlineData("Insert Unit Director", CastRole.None)]
    [InlineData("Action Director", CastRole.None)]
    [InlineData("Stage Director", CastRole.None)]
    [InlineData("Script Supervisor", CastRole.None)]
    [InlineData("Continuity", CastRole.None)]
    [InlineData("Director of Photography", CastRole.None)]
    [InlineData("Art Direction", CastRole.None)]
    [InlineData("Casting", CastRole.None)]
    [InlineData("Casting Director", CastRole.None)]
    [InlineData("Casting Producer", CastRole.None)]
    [InlineData("Production Manager", CastRole.None)]
    [InlineData("Unit Production Manager", CastRole.None)]
    [InlineData("Production Coordinator", CastRole.None)]
    [InlineData("Location Manager", CastRole.None)]
    [InlineData("Post Producer", CastRole.None)]
    [InlineData("Visual Effects Producer", CastRole.None)]
    [InlineData("Music Producer", CastRole.None)]
    [InlineData("Executive In Charge Of Production", CastRole.None)]
    [InlineData("Story Editor", CastRole.None)]
    [InlineData("Executive Story Editor", CastRole.None)]
    [InlineData("Script Consultant", CastRole.None)]
    [InlineData("Script Editor", CastRole.None)]
    [InlineData("Writers' Assistant", CastRole.None)]
    [InlineData("Staff Writer", CastRole.None)]
    [InlineData("Storyboard", CastRole.None)]
    [InlineData("Lyricist", CastRole.None)]
    [InlineData("Other", CastRole.None)]
    [InlineData("", CastRole.None)]
    [InlineData(null, CastRole.None)]
    public void A_job_is_a_director_writer_producer_or_creator_credit_only_when_it_names_one(string? job, CastRole role)
    {
        TmdbCrewJobs.RoleFor(job).Should().Be(role);
    }
}
