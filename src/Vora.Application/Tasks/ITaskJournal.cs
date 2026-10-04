namespace Vora.Application.Tasks;

public interface ITaskJournal
{
    void Record(Guid taskId, string name, TaskRecipe recipe);
    void Complete(Guid taskId);
}
