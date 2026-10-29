namespace Vora.Application.Collections;

public enum CollectionHiddenReason
{
    Empty,
    AutomaticCollectionsHidden,
    BelowMinimumSize,
    OutOfSeason
}

public static class CollectionVisibility
{
    public const int HideAutomatic = 0;
    public const int DefaultMinimum = 3;
    public const int MaxMinimum = 25;

    public static CollectionHiddenReason? HiddenReason(bool systemGenerated, int itemCount, int libraryMinimum, bool inSeason = true)
    {
        if (itemCount <= 0) return CollectionHiddenReason.Empty;
        if (!inSeason) return CollectionHiddenReason.OutOfSeason;
        if (!systemGenerated) return null;
        if (libraryMinimum <= HideAutomatic) return CollectionHiddenReason.AutomaticCollectionsHidden;
        return itemCount < libraryMinimum ? CollectionHiddenReason.BelowMinimumSize : null;
    }

    public static int Clamp(int? requested, int fallback) =>
        requested is int value ? Math.Clamp(value, HideAutomatic, MaxMinimum) : fallback;
}
