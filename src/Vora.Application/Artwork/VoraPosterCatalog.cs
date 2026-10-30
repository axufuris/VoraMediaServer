using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Vora.Application.Artwork;

public sealed record VoraPoster(string Id, string Category, string Title, IReadOnlyList<string> Aliases, string Address, string FilePath);

public sealed record VoraPosterCategory(string Id, string Name);

public interface IVoraPosterCatalog
{
    IReadOnlyList<VoraPosterCategory> Categories { get; }
    IReadOnlyList<VoraPoster> Posters { get; }
    VoraPoster? Find(string? address);
    VoraPoster? Find(string category, string fileName);
}

public sealed class VoraPosterCatalog : IVoraPosterCatalog
{
    private const string ManifestName = "posters.json";

    private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    private readonly Lazy<Shelf> _shelf;

    public VoraPosterCatalog(ILogger<VoraPosterCatalog> logger)
        : this(Path.Combine(AppContext.BaseDirectory, "Assets", "Posters"), logger)
    {
    }

    internal VoraPosterCatalog(string folder, ILogger<VoraPosterCatalog> logger)
    {
        _shelf = new Lazy<Shelf>(() => Load(folder, logger));
    }

    public IReadOnlyList<VoraPosterCategory> Categories => _shelf.Value.Categories;

    public IReadOnlyList<VoraPoster> Posters => _shelf.Value.Posters;

    public VoraPoster? Find(string? address) =>
        VoraPosterAddress.TryParse(address, out var category, out var id) ? Lookup(category, id) : null;

    public VoraPoster? Find(string category, string fileName) =>
        VoraPosterAddress.TryReadFile(category, fileName, out var posterCategory, out var id) ? Lookup(posterCategory, id) : null;

    private VoraPoster? Lookup(string category, string id) =>
        _shelf.Value.ByKey.GetValueOrDefault(Key(category, id));

    private static string Key(string category, string id) => $"{category}/{id}";

    private static Shelf Load(string folder, ILogger logger)
    {
        var manifestPath = Path.Combine(folder, ManifestName);
        if (!File.Exists(manifestPath))
        {
            logger.LogWarning("No Vora posters were found in {Folder}, so the Vora poster picker is empty.", folder);
            return Shelf.Empty;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), ManifestJson);
            if (manifest == null) return Shelf.Empty;

            var posters = manifest.Posters
                .Where(entry => VoraPosterAddress.TryReadFile(entry.Category, $"{entry.Id}.jpg", out _, out _))
                .Select(entry => new VoraPoster(
                    entry.Id,
                    entry.Category,
                    entry.Title,
                    entry.Aliases,
                    VoraPosterAddress.Build(entry.Category, entry.Id, entry.Version),
                    Path.Combine(folder, entry.Category, $"{entry.Id}.jpg")))
                .ToList();

            var categories = manifest.Categories
                .Select(entry => new VoraPosterCategory(entry.Id, entry.Name))
                .ToList();

            return new Shelf(categories, posters, posters.ToDictionary(poster => Key(poster.Category, poster.Id)));
        }
        catch (Exception ex) when (ex is JsonException or IOException or ArgumentException)
        {
            logger.LogWarning(ex, "The Vora poster list in {Folder} could not be read, so the Vora poster picker is empty.", folder);
            return Shelf.Empty;
        }
    }

    private sealed record Shelf(IReadOnlyList<VoraPosterCategory> Categories, IReadOnlyList<VoraPoster> Posters, IReadOnlyDictionary<string, VoraPoster> ByKey)
    {
        public static readonly Shelf Empty = new([], [], new Dictionary<string, VoraPoster>());
    }

    private sealed record Manifest(List<ManifestCategory> Categories, List<ManifestPoster> Posters);

    private sealed record ManifestCategory(string Id, string Name);

    private sealed record ManifestPoster(string Id, string Category, string Title, List<string> Aliases, string Version);
}
