using Vora.Domain.Entities.Media;

namespace Vora.Domain.Tests;

public class MusicNameKeyTests
{
    [Theory]
    [InlineData("Crash My Party", "crash my party")]
    [InlineData("  Crash   My  Party  ", "crash my party")]
    [InlineData("Can’t Take It", "can't take it")]
    [InlineData("“Quoted”", "\"quoted\"")]
    [InlineData("Rock – Roll", "rock - roll")]
    [InlineData("Non breaking", "non breaking")]
    public void Typography_nobody_would_call_a_different_song_is_folded(string input, string expected) =>
        MusicNameKey.Normalize(input).Should().Be(expected);

    // Folding these would hand one recording another's popularity.
    [Theory]
    [InlineData("Kansas - (piano version)", "Kansas")]
    [InlineData("Crash My Party (Deluxe Edition)", "Crash My Party")]
    [InlineData("Stan (feat. Dido)", "Stan")]
    public void A_bracketed_version_is_a_different_title(string version, string original) =>
        MusicNameKey.Normalize(version).Should().NotBe(MusicNameKey.Normalize(original));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_normalises_to_empty(string? input) =>
        MusicNameKey.Normalize(input).Should().BeEmpty();
}
