using Vora.Domain.Entities.Library;
using Vora.Domain.Enums;

namespace Vora.Domain.Tests;

// Four subsystems ask this question — thumbnails, subtitle pre-extraction, the
// scheduled job worker and library creation — so the membership is asserted here
// rather than in any one of their test classes.
public class LibraryCapabilitiesTests
{
    [Theory]
    [InlineData(LibraryType.Movie, true)]
    [InlineData(LibraryType.TvShow, true)]
    [InlineData(LibraryType.HomeVideo, true)]
    [InlineData(LibraryType.Music, false)]
    [InlineData(LibraryType.LiveTv, false)]
    public void HasVideoContent_classifies_library_types(LibraryType type, bool expected) =>
        type.HasVideoContent().Should().Be(expected);

    // Every member of the enum has to land on one side or the other. A new
    // library type defaults to "no video", which is the safe answer — it skips
    // the video-only passes rather than queueing work that finds nothing.
    [Fact]
    public void Every_library_type_is_classified()
    {
        foreach (var type in Enum.GetValues<LibraryType>())
        {
            var _ = type.HasVideoContent();
        }

        Enum.GetValues<LibraryType>().Count(t => t.HasVideoContent()).Should().Be(3);
    }
}
