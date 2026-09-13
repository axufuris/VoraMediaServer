namespace Vora.Application.Calendar;

// A calendar event matched to something already in the library. The item id is
// the point: knowing only which library it sits in tells a client the badge to
// draw but not where to send the viewer, which is why an in-library release used
// to open the discovery page for a title the server already had.
public readonly record struct LibraryMatch(Guid MediaItemId, Guid LibraryId);
