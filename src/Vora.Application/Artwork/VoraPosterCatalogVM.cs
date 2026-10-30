namespace Vora.Application.Artwork;

public class VoraPosterCatalogVM
{
    public List<VoraPosterCategoryVM> Categories { get; set; } = new();
    public List<VoraPosterVM> Posters { get; set; } = new();

    public static VoraPosterCatalogVM From(IVoraPosterCatalog catalog) => new()
    {
        Categories = catalog.Categories.Select(category => new VoraPosterCategoryVM { Id = category.Id, Name = category.Name }).ToList(),
        Posters = catalog.Posters.Select(poster => new VoraPosterVM
        {
            Id = poster.Id,
            Category = poster.Category,
            Title = poster.Title,
            Aliases = poster.Aliases.ToList(),
            Url = poster.Address,
        }).ToList(),
    };
}

public class VoraPosterCategoryVM
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class VoraPosterVM
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public string Url { get; set; } = string.Empty;
}
