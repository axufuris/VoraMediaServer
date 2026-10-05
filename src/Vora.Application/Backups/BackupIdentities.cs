namespace Vora.Application.Backups;

public enum BackupItemKind
{
    MediaItem,
    Album,
    Artist,
    Library,
    Collection,
    Channel
}

public sealed class BackupItemIdentity
{
    public Guid Id { get; set; }
    public List<string> Keys { get; set; } = new();
    public string? Library { get; set; }
}

public sealed class BackupIdentityFile
{
    public List<BackupItemIdentity> MediaItems { get; set; } = new();
    public List<BackupItemIdentity> Albums { get; set; } = new();
    public List<BackupItemIdentity> Artists { get; set; } = new();
    public List<BackupItemIdentity> Libraries { get; set; } = new();
    public List<BackupItemIdentity> Collections { get; set; } = new();
    public List<BackupItemIdentity> Channels { get; set; } = new();

    public static string PathFor(string sectionKey) => $"{sectionKey}/{BackupJson.IdentitiesFileSuffix}";

    public List<BackupItemIdentity> For(BackupItemKind kind) => kind switch
    {
        BackupItemKind.MediaItem => MediaItems,
        BackupItemKind.Album => Albums,
        BackupItemKind.Artist => Artists,
        BackupItemKind.Library => Libraries,
        BackupItemKind.Collection => Collections,
        BackupItemKind.Channel => Channels,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}

public sealed class BackupItemReferences
{
    private readonly Dictionary<BackupItemKind, HashSet<Guid>> _ids = new();

    public BackupItemReferences Add(BackupItemKind kind, Guid id)
    {
        if (!_ids.TryGetValue(kind, out var ids))
        {
            ids = new HashSet<Guid>();
            _ids[kind] = ids;
        }
        ids.Add(id);
        return this;
    }

    public BackupItemReferences Add(BackupItemKind kind, Guid? id) => id.HasValue ? Add(kind, id.Value) : this;

    public BackupItemReferences AddRange(BackupItemKind kind, IEnumerable<Guid> ids)
    {
        foreach (var id in ids)
        {
            Add(kind, id);
        }
        return this;
    }

    public IReadOnlyCollection<Guid> Get(BackupItemKind kind) =>
        _ids.TryGetValue(kind, out var ids) ? ids : Array.Empty<Guid>();

    public IEnumerable<BackupItemKind> Kinds => _ids.Where(p => p.Value.Count > 0).Select(p => p.Key);
}

public sealed class BackupReferenceResolver
{
    private readonly IReadOnlyDictionary<BackupItemKind, HashSet<Guid>> _present;
    private readonly Dictionary<(BackupItemKind Kind, Guid Id), BackupItemIdentity> _recorded = new();
    private readonly Dictionary<(BackupItemKind Kind, string Key), List<BackupItemIdentity>> _current = new();
    private readonly Dictionary<(BackupItemKind Kind, Guid Id), Guid?> _resolved = new();

    public BackupReferenceResolver(
        IReadOnlyDictionary<BackupItemKind, HashSet<Guid>> present,
        BackupIdentityFile? recorded,
        BackupIdentityFile current)
    {
        _present = present;

        foreach (var kind in Enum.GetValues<BackupItemKind>())
        {
            if (recorded != null)
            {
                foreach (var identity in recorded.For(kind))
                {
                    _recorded.TryAdd((kind, identity.Id), identity);
                }
            }

            foreach (var identity in current.For(kind))
            {
                foreach (var key in identity.Keys.Distinct(StringComparer.Ordinal))
                {
                    if (!_current.TryGetValue((kind, key), out var candidates))
                    {
                        candidates = new List<BackupItemIdentity>();
                        _current[(kind, key)] = candidates;
                    }
                    candidates.Add(identity);
                }
            }
        }
    }

    public int RemappedCount { get; private set; }

    public bool IsPresent(BackupItemKind kind, Guid id) =>
        _present.TryGetValue(kind, out var ids) && ids.Contains(id);

    public Guid? Resolve(BackupItemKind kind, Guid? id) => id.HasValue ? Resolve(kind, id.Value) : null;

    public Guid? Resolve(BackupItemKind kind, Guid id)
    {
        if (IsPresent(kind, id)) return id;
        if (_resolved.TryGetValue((kind, id), out var known)) return known;

        var matched = Match(kind, id);
        _resolved[(kind, id)] = matched;
        if (matched.HasValue) RemappedCount++;
        return matched;
    }

    private Guid? Match(BackupItemKind kind, Guid id)
    {
        if (!_recorded.TryGetValue((kind, id), out var identity)) return null;

        foreach (var key in identity.Keys)
        {
            if (!_current.TryGetValue((kind, key), out var candidates) || candidates.Count == 0) continue;

            var sameLibrary = identity.Library == null
                ? new List<BackupItemIdentity>()
                : candidates.Where(c => string.Equals(c.Library, identity.Library, StringComparison.OrdinalIgnoreCase)).ToList();
            var pool = sameLibrary.Count > 0 ? sameLibrary : candidates;
            return pool.OrderBy(c => c.Id).First().Id;
        }

        return null;
    }
}
