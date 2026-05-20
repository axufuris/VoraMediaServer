namespace Vora.Application.Tasks.ViewModels;

public class QueuedTaskVM
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = "Pending";
}