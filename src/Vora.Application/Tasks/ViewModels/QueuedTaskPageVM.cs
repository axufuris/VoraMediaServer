namespace Vora.Application.Tasks.ViewModels;

public class QueuedTaskPageVM
{
    public List<QueuedTaskVM> Items { get; set; } = new();
    public int Total { get; set; }
    public int Running { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}
