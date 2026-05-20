namespace Vora.Domain.Entities.Common;

public abstract class LockableEntity
{
    public List<string> LockedFields { get; set; } = new();

    public bool IsLocked(string fieldName) =>
        LockedFields.Contains(fieldName, StringComparer.OrdinalIgnoreCase);

    public void LockField(string fieldName)
    {
        if (!IsLocked(fieldName)) LockedFields.Add(fieldName);
    }

    public void UnlockField(string fieldName) =>
        LockedFields.RemoveAll(f => f.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
}
