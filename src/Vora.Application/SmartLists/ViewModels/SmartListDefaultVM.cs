using Vora.Domain.Enums;

namespace Vora.Application.SmartLists.ViewModels;

public class SmartListDefaultVM
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public SmartListSource Source { get; set; }
    public bool IsPresent { get; set; }
}
