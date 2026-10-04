using Vora.Domain.Entities.SmartLists;
using Vora.Domain.Enums;

namespace Vora.Application.SmartLists;

public sealed record SmartListDefault(
    string Key,
    Guid Id,
    string Title,
    SmartListSource Source,
    SmartListSortBy SortBy,
    int MaxItems,
    int DisplayOrder,
    string FilterRulesJson)
{
    public SmartList ToEntity() => new()
    {
        Id = Id,
        Title = Title,
        Source = Source,
        DefaultKey = Key,
        SortBy = SortBy,
        MaxItems = MaxItems,
        DisplayOrder = DisplayOrder,
        FilterRulesJson = FilterRulesJson,
        ShowOnHomepage = true,
        ShowToFriends = true
    };
}

public static class SmartListDefaults
{
    public static IReadOnlyList<SmartListDefault> All { get; } =
    [
        new("recently-released-movies-episodes", Guid.Parse("73c33c2c-1fe6-4885-875e-481a1dac5462"), "Recently Released Movies & Episodes",
            SmartListSource.Library, SmartListSortBy.ReleaseDateDesc, 20, 0, "{\"mediaTypes\":[\"Movie\",\"Episode\"]}"),
        new("recently-added-movies-shows", Guid.Parse("17ddede2-2de0-42b8-9b33-32708b4d29b8"), "Recently Added Movies & Shows",
            SmartListSource.Library, SmartListSortBy.DateAddedDesc, 20, 1, "{\"mediaTypes\":[\"Movie\",\"TvShow\",\"Season\",\"Episode\"]}"),
        new("recently-released-movies", Guid.Parse("ebbefd92-4232-4cae-9c5d-2134943b8bf8"), "Recently Released Movies",
            SmartListSource.Library, SmartListSortBy.ReleaseDateDesc, 20, 2, "{\"mediaTypes\":[\"Movie\"]}"),
        new("recently-added-movies", Guid.Parse("c88d6c8a-57ea-4b24-a7be-3f2638a38aca"), "Recently Added Movies",
            SmartListSource.Library, SmartListSortBy.DateAddedDesc, 20, 3, "{\"mediaTypes\":[\"Movie\"]}"),
        new("recently-released-episodes", Guid.Parse("58424b85-b6da-4a9c-8204-e364f1319508"), "Recently Released Episodes",
            SmartListSource.Library, SmartListSortBy.ReleaseDateDesc, 20, 4, "{\"mediaTypes\":[\"Episode\"]}"),
        new("recently-added-shows", Guid.Parse("dfc420d4-421c-4e14-aec4-a5bedefd2f2e"), "Recently Added Shows",
            SmartListSource.Library, SmartListSortBy.DateAddedDesc, 20, 5, "{\"mediaTypes\":[\"TvShow\"]}"),
        new("favorite-channels", Guid.Parse("80d62ff0-9b1a-4381-a03a-2595af4b1d9d"), "Favorite Channels",
            SmartListSource.FavoriteChannels, SmartListSortBy.TitleAsc, 30, 6, "{}"),
        new("recent-recordings", Guid.Parse("1cca6bf0-87a6-4186-82a0-1d1efce4e6b8"), "Recent Recordings",
            SmartListSource.RecentRecordings, SmartListSortBy.DateAddedDesc, 20, 7, "{}"),
        new("recently-added-music", Guid.Parse("666c043f-f0f7-47f8-810b-8b0d5afcaeb9"), "Recently Added Music",
            SmartListSource.RecentlyAddedMusic, SmartListSortBy.DateAddedDesc, 20, 8, "{}"),
        new("new-podcast-episodes", Guid.Parse("2133070b-8810-4b2e-9514-619a682b04b1"), "New Podcast Episodes",
            SmartListSource.NewPodcastEpisodes, SmartListSortBy.DateAddedDesc, 20, 9, "{\"unwatchedOnly\":true,\"days\":14}"),
        new("favorite-stations", Guid.Parse("ee067d88-cd48-4382-8b05-1f75b39020eb"), "Favorite Radio Stations",
            SmartListSource.FavoriteStations, SmartListSortBy.TitleAsc, 30, 10, "{}")
    ];
}
