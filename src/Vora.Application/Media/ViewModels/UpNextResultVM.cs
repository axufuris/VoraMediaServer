namespace Vora.Application.Media.ViewModels;

public class UpNextResultVM
{
    public UpNextItemVM? NextItem { get; set; }
    public UpNextItemVM? PreviousItem { get; set; }
    public List<RelatedListVM> RelatedLists { get; set; } = new();
}
