using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vora.Application.Backups;
using Vora.Domain.Entities.Collections;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.SmartLists;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Backups.Sections;

public sealed class CollectionsBackupSection : IBackupSection
{
    private static readonly BackupRowNoun CollectionNoun = new("collection", "collections");
    private static readonly BackupRowNoun ItemNoun = new("collection entry", "collection entries");

    private readonly VoraDbContext _db;
    private readonly BackupReferenceMapper _references;

    public CollectionsBackupSection(VoraDbContext db, BackupReferenceMapper references)
    {
        _db = db;
        _references = references;
    }

    public string Key => "library.collections";
    public string DisplayName => "Collections";
    public BackupSectionGroup Group => BackupSectionGroup.Library;
    public bool RequiresExplicitConfirm => false;
    public bool CanGrowLarge => false;
    public string? DestructiveWarning =>
        "Replaces the collections an admin created and the titles in them. Collections a library scan creates are left alone.";

    public async Task WriteAsync(IBackupWriter writer, CancellationToken ct)
    {
        var collections = await _db.Collections.AsNoTracking().Where(c => !c.SystemGenerated).ToListAsync(ct);
        var collectionIds = collections.Select(c => c.Id).ToList();
        var items = await _db.CollectionItems.AsNoTracking().Where(i => collectionIds.Contains(i.CollectionId)).ToListAsync(ct);

        await writer.WriteJsonAsync($"{Key}/collections.json", collections, ct);
        await writer.WriteJsonAsync($"{Key}/items.json", items, ct);
        await _references.WriteIdentitiesAsync(writer, Key, References(collections, items), ct);
    }

    public async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var collections = await reader.ReadJsonAsync<List<Collection>>($"{Key}/collections.json", ct);
        if (collections == null) return new BackupSectionImportResult();
        var items = await reader.ReadJsonAsync<List<CollectionItem>>($"{Key}/items.json", ct) ?? new List<CollectionItem>();

        var tally = new BackupSkipTally();
        var resolver = await _references.ReadResolverAsync(reader, Key, References(collections, items), ct);
        var scanCreatedIds = await _db.Collections.Where(c => c.SystemGenerated).Select(c => c.Id).ToHashSetAsync(ct);

        var restoredCollections = new List<Collection>();
        foreach (var collection in collections)
        {
            if (collection.SystemGenerated || scanCreatedIds.Contains(collection.Id))
            {
                tally.Skip(CollectionNoun, BackupSkipReason.Duplicate);
                continue;
            }

            collection.LibraryId = collection.LibraryId.HasValue
                ? resolver.Resolve(BackupItemKind.Library, collection.LibraryId.Value) ?? collection.LibraryId
                : null;
            collection.ExcludedMediaIdsJson = RemapExcluded(collection.ExcludedMediaIdsJson, resolver);
            collection.Items = new List<MediaItem>();
            restoredCollections.Add(collection);
        }

        var restoredIds = restoredCollections.Select(c => c.Id).ToHashSet();
        var restoredItems = new List<CollectionItem>();
        foreach (var item in items.Where(i => restoredIds.Contains(i.CollectionId)))
        {
            var mediaItemId = resolver.Resolve(BackupItemKind.MediaItem, item.MediaItemId);
            if (mediaItemId == null)
            {
                tally.Skip(ItemNoun, BackupSkipReason.MissingItem);
                continue;
            }
            restoredItems.Add(new CollectionItem
            {
                CollectionId = item.CollectionId,
                MediaItemId = mediaItemId.Value,
                SortOrder = item.SortOrder,
                InUniverseYear = item.InUniverseYear,
                InUniverseYearLocked = item.InUniverseYearLocked,
                ManuallyAdded = item.ManuallyAdded,
                AddedAt = item.AddedAt
            });
        }
        restoredItems = BackupTableSync.KeepOnePer(restoredItems, i => (i.CollectionId, i.MediaItemId), i => i.AddedAt, tally, ItemNoun);

        await BackupTableSync.ReplaceAsync(_db, _db.Collections.Where(c => !c.SystemGenerated), restoredCollections, ct);
        await BackupTableSync.ReplaceAsync(_db, _db.CollectionItems.Where(i => !i.Collection.SystemGenerated), restoredItems, ct);

        return tally.ToResult(restoredCollections.Count + restoredItems.Count);
    }

    private static string? RemapExcluded(string? json, BackupReferenceResolver resolver)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;

        List<Guid>? ids;
        try
        {
            ids = JsonSerializer.Deserialize<List<Guid>>(json);
        }
        catch (JsonException)
        {
            return json;
        }
        if (ids == null) return json;

        var remapped = ids
            .Select(id => resolver.Resolve(BackupItemKind.MediaItem, id))
            .OfType<Guid>()
            .Distinct()
            .ToList();
        return JsonSerializer.Serialize(remapped);
    }

    private static List<Guid> ExcludedIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<Guid>();
        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json) ?? new List<Guid>();
        }
        catch (JsonException)
        {
            return new List<Guid>();
        }
    }

    private static BackupItemReferences References(List<Collection> collections, List<CollectionItem> items) =>
        new BackupItemReferences()
            .AddRange(BackupItemKind.Library, collections.Select(c => c.LibraryId).OfType<Guid>())
            .AddRange(BackupItemKind.MediaItem, items.Select(i => i.MediaItemId))
            .AddRange(BackupItemKind.MediaItem, collections.SelectMany(c => ExcludedIds(c.ExcludedMediaIdsJson)));
}

public sealed class SmartListsBackupSection : EntityTableBackupSection<SmartList>
{
    private static readonly BackupRowNoun Noun = new("smart list", "smart lists");

    private readonly BackupReferenceMapper _references;

    public SmartListsBackupSection(VoraDbContext db, BackupReferenceMapper references) : base(db)
    {
        _references = references;
    }

    public override string Key => "library.smart-lists";
    public override string DisplayName => "Smart Lists";
    public override BackupSectionGroup Group => BackupSectionGroup.Library;
    protected override DbSet<SmartList> Set(VoraDbContext db) => db.SmartLists;

    protected override Task WriteReferencesAsync(IBackupWriter writer, List<SmartList> rows, CancellationToken ct) =>
        _references.WriteIdentitiesAsync(writer, Key, References(rows), ct);

    protected override async Task<List<SmartList>> PrepareRowsAsync(IBackupReader reader, List<SmartList> rows, BackupSkipTally tally, CancellationToken ct)
    {
        var resolver = await _references.ReadResolverAsync(reader, Key, References(rows), ct);
        var restorable = new List<SmartList>();
        foreach (var row in rows)
        {
            if (row.LibraryId.HasValue)
            {
                var libraryId = resolver.Resolve(BackupItemKind.Library, row.LibraryId.Value);
                if (libraryId == null)
                {
                    tally.Skip(Noun, BackupSkipReason.MissingLibrary);
                    continue;
                }
                row.LibraryId = libraryId;
            }

            if (row.CollectionId.HasValue)
            {
                var collectionId = resolver.Resolve(BackupItemKind.Collection, row.CollectionId.Value);
                if (collectionId == null)
                {
                    tally.Skip(Noun, BackupSkipReason.MissingCollection);
                    continue;
                }
                row.CollectionId = collectionId;
            }

            row.Collection = null;
            restorable.Add(row);
        }
        return restorable;
    }

    private static BackupItemReferences References(List<SmartList> rows) =>
        new BackupItemReferences()
            .AddRange(BackupItemKind.Library, rows.Select(r => r.LibraryId).OfType<Guid>())
            .AddRange(BackupItemKind.Collection, rows.Select(r => r.CollectionId).OfType<Guid>());
}

public sealed class DedupeRulesBackupSection : IBackupSection
{
    private static readonly BackupRowNoun RuleNoun = new("dedupe rule", "dedupe rules");
    private static readonly BackupRowNoun IgnoredNoun = new("ignored duplicate", "ignored duplicates");

    private readonly VoraDbContext _db;
    private readonly BackupReferenceMapper _references;

    public DedupeRulesBackupSection(VoraDbContext db, BackupReferenceMapper references)
    {
        _db = db;
        _references = references;
    }

    public string Key => "library.dedupe-rules";
    public string DisplayName => "Dedupe Rules & Ignored Duplicates";
    public BackupSectionGroup Group => BackupSectionGroup.Library;
    public bool RequiresExplicitConfirm => false;
    public bool CanGrowLarge => false;
    public string? DestructiveWarning => null;

    public async Task WriteAsync(IBackupWriter writer, CancellationToken ct)
    {
        var rules = await _db.MediaDedupeSettings.AsNoTracking().ToListAsync(ct);
        var ignored = await _db.MediaDedupeIgnoredGroups.AsNoTracking().ToListAsync(ct);

        await writer.WriteJsonAsync($"{Key}/rows.json", rules, ct);
        await writer.WriteJsonAsync($"{Key}/ignored-groups.json", ignored, ct);
        await _references.WriteIdentitiesAsync(writer, Key, References(rules, ignored), ct);
    }

    public async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var rules = await reader.ReadJsonAsync<List<MediaDedupeSettings>>($"{Key}/rows.json", ct);
        var ignored = await reader.ReadJsonAsync<List<MediaDedupeIgnoredGroup>>($"{Key}/ignored-groups.json", ct);
        if (rules == null && ignored == null) return new BackupSectionImportResult();

        var tally = new BackupSkipTally();
        var resolver = await _references.ReadResolverAsync(reader, Key, References(rules ?? new(), ignored ?? new()), ct);
        var imported = 0;

        if (rules != null)
        {
            var restorable = new List<MediaDedupeSettings>();
            foreach (var rule in rules)
            {
                if (rule.LibraryId.HasValue)
                {
                    var libraryId = resolver.Resolve(BackupItemKind.Library, rule.LibraryId.Value);
                    if (libraryId == null)
                    {
                        tally.Skip(RuleNoun, BackupSkipReason.MissingLibrary);
                        continue;
                    }
                    rule.LibraryId = libraryId;
                }
                rule.Library = null;
                restorable.Add(rule);
            }

            var global = restorable.Where(r => r.LibraryId == null).ToList();
            var perLibrary = BackupTableSync.KeepOnePer(restorable.Where(r => r.LibraryId != null), r => r.LibraryId ?? Guid.Empty, r => r.UpdatedAt, tally, RuleNoun);
            restorable = global.Concat(perLibrary).ToList();

            await BackupTableSync.ReplaceAsync(_db, _db.MediaDedupeSettings, restorable, ct);
            imported += restorable.Count;
        }

        if (ignored != null)
        {
            var restorable = new List<MediaDedupeIgnoredGroup>();
            foreach (var group in ignored)
            {
                var mediaItemId = resolver.Resolve(BackupItemKind.MediaItem, group.MediaItemId);
                if (mediaItemId == null)
                {
                    tally.Skip(IgnoredNoun, BackupSkipReason.MissingItem);
                    continue;
                }
                group.MediaItemId = mediaItemId.Value;
                restorable.Add(group);
            }
            restorable = BackupTableSync.KeepOnePer(restorable, g => (g.MediaItemId, g.Resolution), g => g.IgnoredAt, tally, IgnoredNoun);

            await BackupTableSync.ReplaceAsync(_db, _db.MediaDedupeIgnoredGroups, restorable, ct);
            imported += restorable.Count;
        }

        return tally.ToResult(imported);
    }

    private static BackupItemReferences References(List<MediaDedupeSettings> rules, List<MediaDedupeIgnoredGroup> ignored) =>
        new BackupItemReferences()
            .AddRange(BackupItemKind.Library, rules.Select(r => r.LibraryId).OfType<Guid>())
            .AddRange(BackupItemKind.MediaItem, ignored.Select(g => g.MediaItemId));
}
