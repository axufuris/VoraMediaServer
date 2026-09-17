using Vora.Application.Calendar;
using Vora.Plugins.Dtos;

namespace Vora.Application.Tests.Calendar;

// Linking a calendar event to the library used to be a side effect of the
// access check, which returns early for anyone with all-library access. So for
// an admin it never ran: every release the server already held looked external,
// showed no In Library dot, and opened the discovery page for a film sitting in
// their own library. Linking is now its own step that runs for everyone.
public class CalendarLibraryLinkingTests
{
    private static readonly Guid ItemId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid LibraryId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherLibraryId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static Dictionary<string, LibraryMatch> Map(string externalId = "1368337") =>
        new() { [externalId] = new LibraryMatch(ItemId, LibraryId) };

    private static CalendarEventDto Event(string? externalId = "1368337") =>
        new() { Title = "The Odyssey", MediaType = "Movie", ExternalId = externalId };

    [Fact]
    public void A_matched_event_carries_the_item_it_matched()
    {
        var ev = Event();

        CalendarManager.LinkToLibrary(ev, Map());

        ev.LibraryItemId.Should().Be(ItemId);
        ev.LibraryId.Should().Be(LibraryId);
        ev.IsInLibrary.Should().BeTrue();
    }

    // Without the item id a client knows to draw the badge but not where to send
    // the viewer, which is the whole bug.
    [Fact]
    public void A_matched_event_is_not_left_with_only_a_library_id()
    {
        var ev = Event();

        CalendarManager.LinkToLibrary(ev, Map());

        ev.LibraryItemId.Should().NotBeNull();
    }

    [Fact]
    public void An_unmatched_event_stays_external()
    {
        var ev = Event("999999");

        CalendarManager.LinkToLibrary(ev, Map());

        ev.LibraryItemId.Should().BeNull();
        ev.IsInLibrary.Should().BeFalse();
    }

    [Fact]
    public void An_event_with_no_external_id_is_left_alone()
    {
        var ev = Event(externalId: null);

        CalendarManager.LinkToLibrary(ev, Map());

        ev.IsInLibrary.Should().BeFalse();
    }

    // The local provider already knows the item; that must not be overwritten,
    // and it counts as in-library whether or not the external map has it.
    [Fact]
    public void An_event_that_already_knows_its_item_keeps_it()
    {
        var ev = Event("999999");
        ev.LibraryItemId = ItemId;

        CalendarManager.LinkToLibrary(ev, Map());

        ev.LibraryItemId.Should().Be(ItemId);
        ev.IsInLibrary.Should().BeTrue();
    }

    [Fact]
    public void Linking_runs_for_an_all_access_profile_too()
    {
        var ev = Event();

        CalendarManager.LinkToLibrary(ev, Map());

        CalendarManager.PassesLibraryAccess(ev, hasAllAccess: true, allowedLibs: []).Should().BeTrue();
        ev.IsInLibrary.Should().BeTrue();
    }

    [Fact]
    public void A_restricted_profile_still_cannot_see_another_librarys_item()
    {
        var ev = Event();
        CalendarManager.LinkToLibrary(ev, Map());

        CalendarManager.PassesLibraryAccess(ev, hasAllAccess: false, allowedLibs: [OtherLibraryId]).Should().BeFalse();
    }

    [Fact]
    public void A_restricted_profile_sees_an_item_in_a_library_it_can_reach()
    {
        var ev = Event();
        CalendarManager.LinkToLibrary(ev, Map());

        CalendarManager.PassesLibraryAccess(ev, hasAllAccess: false, allowedLibs: [LibraryId]).Should().BeTrue();
    }

    // An upcoming release nobody owns belongs on the calendar whatever the
    // profile can reach — it is in no library to be excluded from.
    [Fact]
    public void An_unowned_release_survives_a_restricted_profile()
    {
        var ev = Event("999999");
        CalendarManager.LinkToLibrary(ev, Map());

        CalendarManager.PassesLibraryAccess(ev, hasAllAccess: false, allowedLibs: [OtherLibraryId]).Should().BeTrue();
    }
}
