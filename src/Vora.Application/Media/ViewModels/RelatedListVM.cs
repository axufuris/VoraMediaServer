namespace Vora.Application.Media.ViewModels;

public class RelatedListVM
{
    public string Title { get; set; } = string.Empty;
    public List<UpNextItemVM> Items { get; set; } = new();
}
