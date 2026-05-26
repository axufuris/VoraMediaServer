using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vora.Api.Tests.Infra;

namespace Vora.Api.Tests;

public class LibraryCrudFlowTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public LibraryCrudFlowTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient AdminClient()
    {
        var token = JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: true);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Round_trip_create_list_get()
    {
        // Note: DELETE is not exercised here because LibraryRepository.DeleteLibraryAsync
        // uses ExecuteDeleteAsync() which is relational-only — EF Core InMemory throws
        // InvalidOperationException for it. Delete coverage will need Testcontainers + real
        // Postgres in a future test phase.
        var client = AdminClient();

        // CREATE
        var create = await client.PostAsJsonAsync("/api/libraries", new
        {
            name = "Round Trip Library " + Guid.NewGuid().ToString("N")[..8],
            type = "Movie",
            folderPaths = new[] { "/media/round-trip" }
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await create.Content.ReadFromJsonAsync<CreatedLibrary>();
        created.Should().NotBeNull();
        created!.Id.Should().NotBe(Guid.Empty);

        // LIST should contain the new id
        var list = await client.GetAsync("/api/libraries");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var libraries = await list.Content.ReadFromJsonAsync<List<LibrarySummary>>();
        libraries.Should().NotBeNull();
        libraries!.Should().Contain(l => l.Id == created.Id);

        // GET by id
        var get = await client.GetAsync($"/api/libraries/{created.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var details = await get.Content.ReadFromJsonAsync<LibraryDetails>();
        details.Should().NotBeNull();
        details!.Id.Should().Be(created.Id);
        details.FolderPaths.Should().Contain("/media/round-trip");
    }

    [Fact]
    public async Task GET_library_by_unknown_id_returns_404()
    {
        var client = AdminClient();

        var response = await client.GetAsync($"/api/libraries/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed class CreatedLibrary
    {
        public Guid Id { get; set; }
    }

    private sealed class LibrarySummary
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    private sealed class LibraryDetails
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public List<string> FolderPaths { get; set; } = new();
    }
}
