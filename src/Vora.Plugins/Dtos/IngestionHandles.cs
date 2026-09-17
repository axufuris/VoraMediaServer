namespace Vora.Plugins.Dtos;

public readonly record struct LibraryHandle
{
    internal Guid Value { get; }

    internal LibraryHandle(Guid value)
    {
        Value = value;
    }

    public static LibraryHandle FromGuid(Guid value) => new(value);
}

public readonly record struct MediaItemHandle
{
    internal Guid Value { get; }

    internal MediaItemHandle(Guid value)
    {
        Value = value;
    }
}

public readonly record struct SeasonHandle
{
    internal Guid Value { get; }

    internal SeasonHandle(Guid value)
    {
        Value = value;
    }
}

public readonly record struct ArtistHandle
{
    internal Guid Value { get; }

    internal ArtistHandle(Guid value)
    {
        Value = value;
    }
}

public readonly record struct AlbumHandle
{
    internal Guid Value { get; }

    internal AlbumHandle(Guid value)
    {
        Value = value;
    }
}
