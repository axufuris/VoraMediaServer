using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Artwork;

namespace Vora.Application.Tests.Artwork;

public class VoraPosterCatalogTests : IDisposable
{
    private static readonly string ShippedFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Posters");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vora-posters-" + Guid.NewGuid().ToString("N"));

    public VoraPosterCatalogTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static VoraPosterCatalog Shipped() => new(NullLogger<VoraPosterCatalog>.Instance);

    private VoraPosterCatalog InFolder() => new(_directory, NullLogger<VoraPosterCatalog>.Instance);

    [Fact]
    public void All_75_posters_ship_in_five_categories()
    {
        var catalog = Shipped();

        catalog.Categories.Select(category => category.Id).Should().Equal("holidays", "popular", "genres", "decades", "ratings");
        catalog.Posters.Should().HaveCount(75);
        catalog.Posters.Select(poster => poster.Id).Should().OnlyHaveUniqueItems();
        catalog.Posters.Select(poster => poster.Category).Should().OnlyContain(category => catalog.Categories.Any(known => known.Id == category));
    }

    [Fact]
    public void Every_listed_poster_has_its_file_and_its_address_carries_that_file_s_version()
    {
        foreach (var poster in Shipped().Posters)
        {
            File.Exists(poster.FilePath).Should().BeTrue(poster.Id);
            var version = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(poster.FilePath)))[..12];
            poster.Address.Should().Be($"/api/artwork/vora/{poster.Category}/{poster.Id}.jpg?v={version}", "the address must change when the art does, or every cache keeps the old picture");
        }
    }

    [Fact]
    public void Every_poster_file_in_the_folder_is_listed()
    {
        var listed = Shipped().Posters.Select(poster => Path.GetFullPath(poster.FilePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Directory.GetFiles(ShippedFolder, "*", SearchOption.AllDirectories)
            .Where(file => !file.EndsWith("posters.json", StringComparison.Ordinal))
            .Select(Path.GetFullPath)
            .Should().OnlyContain(file => listed.Contains(file));
    }

    [Fact]
    public void Each_poster_has_lowercase_words_that_find_it_and_no_two_posters_share_one()
    {
        var posters = Shipped().Posters;

        posters.Should().OnlyContain(poster => poster.Aliases.Count > 0);
        posters.SelectMany(poster => poster.Aliases).Should().OnlyContain(alias => alias.Length > 0 && alias == alias.Trim().ToLowerInvariant());
        posters.SelectMany(poster => poster.Aliases).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void An_address_from_an_older_version_finds_the_poster_and_its_current_address()
    {
        var catalog = Shipped();
        var halloween = catalog.Posters.Single(poster => poster.Id == "halloween");

        catalog.Find("/api/artwork/vora/holidays/halloween.jpg?v=0123456789ab").Should().Be(halloween);
        catalog.Find("/api/artwork/vora/holidays/halloween.jpg").Should().Be(halloween);
        catalog.Find("holidays", "halloween.jpg").Should().Be(halloween);
    }

    [Theory]
    [InlineData("/api/artwork/vora/holidays/../posters.json")]
    [InlineData("/api/artwork/vora/holidays/halloween.png")]
    [InlineData("/api/artwork/vora/Holidays/halloween.jpg")]
    [InlineData("/api/artwork/vora/holidays/gone.jpg")]
    [InlineData("/api/artwork/vora/halloween.jpg")]
    [InlineData("/api/artwork/custom/halloween.jpg")]
    [InlineData("https://example.com/api/artwork/vora/holidays/halloween.jpg")]
    [InlineData(null)]
    public void Nothing_but_a_shipped_poster_is_found(string? address)
    {
        Shipped().Find(address).Should().BeNull();
    }

    [Theory]
    [InlineData("holidays", "..%2Fposters.json")]
    [InlineData("..", "posters.json")]
    [InlineData("holidays", "halloween")]
    [InlineData("holidays", "HALLOWEEN.jpg")]
    public void A_file_request_outside_the_shipped_posters_is_not_found(string category, string fileName)
    {
        Shipped().Find(category, fileName).Should().BeNull();
    }

    [Fact]
    public void Without_a_poster_list_the_catalog_is_empty()
    {
        var catalog = InFolder();

        catalog.Posters.Should().BeEmpty();
        catalog.Categories.Should().BeEmpty();
        catalog.Find("/api/artwork/vora/holidays/halloween.jpg").Should().BeNull();
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{ "categories": [], "posters": [ { "id": "halloween", "category": "holidays", "title": null, "aliases": [], "version": "1" } ] }""")]
    [InlineData("""{ "categories": [], "posters": [ { "id": "halloween", "category": "holidays", "aliases": [], "version": "1" } ] }""")]
    public void A_broken_poster_list_leaves_the_catalog_empty(string manifest)
    {
        File.WriteAllText(Path.Combine(_directory, "posters.json"), manifest);

        InFolder().Posters.Should().BeEmpty();
    }

    [Fact]
    public void A_listed_poster_whose_id_is_not_a_safe_file_name_is_left_out()
    {
        File.WriteAllText(Path.Combine(_directory, "posters.json"), """
            {
              "categories": [ { "id": "holidays", "name": "Holidays" } ],
              "posters": [
                { "id": "../escape", "category": "holidays", "title": "Escape", "aliases": ["escape"], "version": "1" },
                { "id": "halloween", "category": "holidays", "title": "Halloween", "aliases": ["halloween"], "version": "abc" }
              ]
            }
            """);

        var catalog = InFolder();

        catalog.Posters.Select(poster => poster.Id).Should().Equal("halloween");
        catalog.Posters.Single().FilePath.Should().Be(Path.Combine(_directory, "holidays", "halloween.jpg"));
        catalog.Posters.Single().Address.Should().Be("/api/artwork/vora/holidays/halloween.jpg?v=abc");
    }
}
