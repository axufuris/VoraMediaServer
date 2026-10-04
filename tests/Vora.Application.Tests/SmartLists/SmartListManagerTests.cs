using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.SmartLists;
using Vora.Application.SmartLists.Dtos;
using Vora.Application.SmartLists.Requests;
using Vora.Application.SmartLists.ViewModels;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.SmartLists;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.SmartLists;

public class SmartListManagerTests
{
    private readonly ISmartListRepository _repository = Substitute.For<ISmartListRepository>();
    private readonly ISmartListSourceResolver _resolver = Substitute.For<ISmartListSourceResolver>();
    private readonly ISystemSettingsRepository _settingsRepository = Substitute.For<ISystemSettingsRepository>();
    private readonly IClientNotifier _notifier = Substitute.For<IClientNotifier>();
    private readonly ServerSetting _settings = new();
    private readonly SmartListManager _manager;

    private static readonly SmartListViewer Viewer = new(
        Guid.NewGuid(), Guid.NewGuid(), false, true, new List<Guid>(), new List<string>(), new List<string>(), false, new MusicAccessFilter());

    public SmartListManagerTests()
    {
        _settingsRepository.GetSettingsAsync().Returns(_settings);
        _repository.GetSmartListItemsAsync(
                Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<SmartListRulesDto?>(), Arg.Any<SmartListSortBy>(), Arg.Any<int>(),
                Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<List<Guid>?>(), Arg.Any<List<string>?>(), Arg.Any<List<string>?>(), Arg.Any<bool>())
            .Returns(new List<LibraryItemVM>());
        _resolver.ResolveAsync(Arg.Any<SmartList>(), Arg.Any<SmartListRulesDto?>(), Arg.Any<SmartListViewer>()).Returns(new List<SmartListEntryVM>());
        _manager = new SmartListManager(_repository, _resolver, _settingsRepository, _notifier, NullLogger<SmartListManager>.Instance);
    }

    private SmartList Stored(SmartListSource source, Action<SmartList>? configure = null)
    {
        var list = new SmartList { Id = Guid.NewGuid(), Title = "Row", Source = source };
        configure?.Invoke(list);
        _repository.GetListByIdAsync(list.Id).Returns(list);
        return list;
    }

    private static SmartListClientVM Client(SmartListSource source) => new() { Id = Guid.NewGuid(), Title = source.ToString(), Source = source };

    [Fact]
    public async Task Rows_for_features_that_are_turned_off_are_not_offered()
    {
        _settings.EnableLiveTv = false;
        _settings.EnablePodcasts = false;
        _repository.GetActiveClientListsAsync(false).Returns(new List<SmartListClientVM>
        {
            Client(SmartListSource.Library),
            Client(SmartListSource.FavoriteChannels),
            Client(SmartListSource.FavoriteStations),
            Client(SmartListSource.NewPodcastEpisodes),
            Client(SmartListSource.RecentlyAddedMusic),
            Client(SmartListSource.RecentRecordings)
        });

        var lists = await _manager.GetActiveSmartListsAsync(false);

        lists.Select(l => l.Source).Should().Equal(SmartListSource.Library, SmartListSource.FavoriteStations, SmartListSource.RecentlyAddedMusic);
    }

    [Fact]
    public void Recordings_need_both_live_tv_and_the_dvr()
    {
        _settings.EnableLiveTv = true;
        _settings.EnableDvr = false;

        SmartListManager.IsSourceEnabled(SmartListSource.RecentRecordings, _settings).Should().BeFalse();
        SmartListManager.IsSourceEnabled(SmartListSource.FavoriteChannels, _settings).Should().BeTrue();
    }

    [Fact]
    public async Task A_library_list_reads_from_the_library_it_is_set_to()
    {
        var libraryId = Guid.NewGuid();
        var list = Stored(SmartListSource.Library, l => l.LibraryId = libraryId);

        await _manager.GetSmartListEntriesAsync(list.Id, Viewer);

        await _repository.Received(1).GetSmartListItemsAsync(
            Viewer.ProfileId, libraryId, Arg.Any<SmartListRulesDto?>(), Arg.Any<SmartListSortBy>(), Arg.Any<int>(),
            Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<List<Guid>?>(), Arg.Any<List<string>?>(), Arg.Any<List<string>?>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task Library_items_come_back_as_media_entries()
    {
        var list = Stored(SmartListSource.Library);
        var track = new LibraryItemVM { Id = Guid.NewGuid(), Title = "Hey Ya!", Type = "Track", Artist = "OutKast", PosterUrl = "/art.jpg" };
        _repository.GetSmartListItemsAsync(
                Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<SmartListRulesDto?>(), Arg.Any<SmartListSortBy>(), Arg.Any<int>(),
                Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<List<Guid>?>(), Arg.Any<List<string>?>(), Arg.Any<List<string>?>(), Arg.Any<bool>())
            .Returns(new List<LibraryItemVM> { track });

        var entries = await _manager.GetSmartListEntriesAsync(list.Id, Viewer);

        entries.Should().ContainSingle();
        entries[0].Kind.Should().Be(SmartListEntryKind.Media);
        entries[0].Subtitle.Should().Be("OutKast");
        entries[0].ImageUrl.Should().Be("/art.jpg");
        entries[0].Media.Should().BeSameAs(track);
    }

    [Fact]
    public async Task Other_sources_go_to_the_resolver_with_their_rules()
    {
        var list = Stored(SmartListSource.NewPodcastEpisodes, l => l.FilterRulesJson = "{\"unwatchedOnly\":true,\"days\":7}");

        await _manager.GetSmartListEntriesAsync(list.Id, Viewer);

        await _resolver.Received(1).ResolveAsync(list, Arg.Is<SmartListRulesDto?>(r => r != null && r.UnwatchedOnly == true && r.Days == 7), Viewer);
    }

    [Fact]
    public async Task A_source_whose_feature_is_off_resolves_to_nothing()
    {
        _settings.EnableInternetRadio = false;
        var list = Stored(SmartListSource.FavoriteStations);

        (await _manager.GetSmartListEntriesAsync(list.Id, Viewer)).Should().BeEmpty();
        await _resolver.DidNotReceive().ResolveAsync(Arg.Any<SmartList>(), Arg.Any<SmartListRulesDto?>(), Arg.Any<SmartListViewer>());
    }

    [Fact]
    public async Task The_old_items_route_returns_nothing_for_rows_that_are_not_library_titles()
    {
        var list = Stored(SmartListSource.FavoriteChannels);

        (await _manager.GetSmartListItemsAsync(list.Id, Viewer)).Should().BeEmpty();
    }

    [Fact]
    public async Task Saving_keeps_the_seasonal_window_and_clamps_the_size()
    {
        var created = new List<SmartList>();
        await _repository.CreateListAsync(Arg.Do<SmartList>(created.Add));

        await _manager.CreateListAsync(new SmartListSaveRequest
        {
            Title = "  Holiday  ",
            MaxItems = 500,
            ActiveStartMonth = 12, ActiveStartDay = 1, ActiveEndMonth = 1, ActiveEndDay = 6,
            LibraryId = Guid.NewGuid()
        });

        var saved = created.Single();
        saved.Title.Should().Be("Holiday");
        saved.MaxItems.Should().Be(SmartListManager.MaxListItems);
        saved.ActiveStartMonth.Should().Be(12);
        saved.ActiveEndDay.Should().Be(6);
        saved.LibraryId.Should().NotBeNull();
    }

    [Fact]
    public async Task A_window_on_a_day_that_does_not_exist_is_dropped_rather_than_breaking_the_home_screen()
    {
        var created = new List<SmartList>();
        await _repository.CreateListAsync(Arg.Do<SmartList>(created.Add));

        await _manager.CreateListAsync(new SmartListSaveRequest { Title = "Bad", ActiveStartMonth = 2, ActiveStartDay = 31, ActiveEndMonth = 3, ActiveEndDay = 1 });

        var saved = created.Single();
        saved.ActiveStartMonth.Should().BeNull();
        saved.ActiveEndMonth.Should().BeNull();
    }

    [Fact]
    public async Task Collections_and_libraries_only_stay_on_the_sources_that_use_them()
    {
        var list = Stored(SmartListSource.Library, l => { l.CollectionId = Guid.NewGuid(); l.LibraryId = Guid.NewGuid(); });

        await _manager.UpdateListAsync(list.Id, new SmartListSaveRequest
        {
            Title = "Favorites",
            Source = SmartListSource.FavoriteChannels,
            CollectionId = list.CollectionId,
            LibraryId = list.LibraryId
        });

        list.CollectionId.Should().BeNull();
        list.LibraryId.Should().BeNull();
        list.Source.Should().Be(SmartListSource.FavoriteChannels);
    }

    [Fact]
    public async Task Restoring_adds_back_only_the_deleted_defaults_after_the_last_row()
    {
        var present = SmartListDefaults.All.Select(d => d.Key).Where(k => k != "favorite-channels" && k != "recent-recordings").ToHashSet();
        _repository.GetDefaultKeysAsync().Returns(present);
        _repository.GetMaxDisplayOrderAsync().Returns(12);
        var created = new List<SmartList>();
        await _repository.CreateListAsync(Arg.Do<SmartList>(created.Add));

        var restored = await _manager.RestoreDefaultListsAsync();

        restored.Should().Be(2);
        created.Select(l => (l.DefaultKey, l.DisplayOrder)).Should().Equal(("favorite-channels", 13), ("recent-recordings", 14));
        await _notifier.Received(1).NotifySmartListsUpdatedAsync();
    }

    [Fact]
    public async Task Nothing_to_restore_changes_nothing()
    {
        _repository.GetDefaultKeysAsync().Returns(SmartListDefaults.All.Select(d => d.Key).ToHashSet());

        (await _manager.RestoreDefaultListsAsync()).Should().Be(0);
        await _notifier.DidNotReceive().NotifySmartListsUpdatedAsync();
    }

    [Fact]
    public void Every_default_has_its_own_key_and_id()
    {
        SmartListDefaults.All.Select(d => d.Key).Should().OnlyHaveUniqueItems();
        SmartListDefaults.All.Select(d => d.Id).Should().OnlyHaveUniqueItems();
        SmartListDefaults.All.Select(d => d.Source).Distinct().Should().HaveCount(Enum.GetValues<SmartListSource>().Length);
    }
}
