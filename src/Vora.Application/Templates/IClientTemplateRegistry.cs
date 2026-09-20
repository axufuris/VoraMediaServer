namespace Vora.Application.Templates;

public interface IClientTemplateRegistry
{
    IReadOnlyList<TemplateMetaVM> GetAll();
    TemplateMetaVM? Get(string id);
    bool Exists(string id);
    string? GetManifestJson(string id);
}

public class ClientTemplateRegistry : IClientTemplateRegistry
{
    private static readonly IReadOnlyList<TemplateMetaVM> BuiltIn = new List<TemplateMetaVM>
    {
        new()
        {
            Id = "vora-cinema",
            Name = "Vora Cinema",
            Version = "1.0.0",
            Author = "Vora",
            Description = "The default. Deep canvas, amber accent, subtle vignette. Designed to disappear behind your library.",
            IsBuiltIn = true,
        },
        new()
        {
            Id = "vora-noir",
            Name = "Vora Noir",
            Version = "1.0.0",
            Author = "Vora",
            Description = "Pure black canvas, cool steel accent, high contrast. For OLED screens and high-glare rooms.",
            IsBuiltIn = true,
        },
        new()
        {
            Id = "vora-velvet",
            Name = "Vora Velvet",
            Version = "1.0.0",
            Author = "Vora",
            Description = "Burgundy canvas with sepia-tinted artwork and gold accents. Holiday-warm without losing cinematic gravitas.",
            IsBuiltIn = true,
        },
        new()
        {
            Id = "vora-aurora",
            Name = "Vora Aurora",
            Version = "1.0.0",
            Author = "Vora",
            Description = "Deep navy with teal accent and an aurora gradient canvas. Cool, ambient, easy on late-night eyes.",
            IsBuiltIn = true,
        },
    };

    private readonly IClientTemplateBundleLoader _bundles;

    public ClientTemplateRegistry(IClientTemplateBundleLoader bundles)
    {
        _bundles = bundles;
    }

    public IReadOnlyList<TemplateMetaVM> GetAll()
    {
        var result = new List<TemplateMetaVM>(BuiltIn);
        var bundleMetas = _bundles.GetBundles()
            .Select(b => new TemplateMetaVM
            {
                Id = b.Id,
                Name = b.Name,
                Version = b.Version,
                Author = b.Author,
                Description = b.Description,
                Preview = b.PreviewRelativePath is null ? null : $"/api/templates/{b.Id}/assets/{b.PreviewRelativePath}",
                IsBuiltIn = false,
            })
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase);
        result.AddRange(bundleMetas);
        return result;
    }

    public TemplateMetaVM? Get(string id)
    {
        var builtIn = BuiltIn.FirstOrDefault(t => t.Id.Equals(id, StringComparison.Ordinal));
        if (builtIn != null) return builtIn;

        var bundle = _bundles.Get(id);
        if (bundle == null) return null;
        return new TemplateMetaVM
        {
            Id = bundle.Id,
            Name = bundle.Name,
            Version = bundle.Version,
            Author = bundle.Author,
            Description = bundle.Description,
            Preview = bundle.PreviewRelativePath is null ? null : $"/api/templates/{bundle.Id}/assets/{bundle.PreviewRelativePath}",
            IsBuiltIn = false,
        };
    }

    public bool Exists(string id) => Get(id) is not null;

    public string? GetManifestJson(string id) => _bundles.Get(id)?.RawManifestJson;
}
