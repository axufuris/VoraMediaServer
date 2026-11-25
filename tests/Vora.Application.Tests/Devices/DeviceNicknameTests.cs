using Vora.Application.Devices;

namespace Vora.Application.Tests.Devices;

public class DeviceNicknameTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   \t ", "")]
    [InlineData("Living room", "Living room")]
    [InlineData("  Living   room  ", "Living room")]
    [InlineData("Living\troom\nTV", "Living room TV")]
    [InlineData("Dad's phone", "Dad's phone")]
    [InlineData("Café 📺", "Café 📺")]
    public void A_nickname_is_trimmed_and_its_runs_of_white_space_become_one_space(string? given, string expected)
    {
        DeviceNickname.Normalize(given).Should().Be(expected);
    }

    [Fact]
    public void A_nickname_of_exactly_the_longest_length_is_kept()
    {
        var longest = new string('a', DeviceNickname.MaxLength);

        DeviceNickname.Normalize(longest).Should().Be(longest);
    }

    [Fact]
    public void A_nickname_longer_than_the_column_is_refused()
    {
        var act = () => DeviceNickname.Normalize(new string('a', DeviceNickname.MaxLength + 1));

        act.Should().Throw<ArgumentException>().WithMessage($"*at most {DeviceNickname.MaxLength}*");
    }

    [Fact]
    public void The_length_is_counted_after_the_spaces_are_tidied()
    {
        var padded = "  " + new string('a', DeviceNickname.MaxLength) + "   ";

        DeviceNickname.Normalize(padded).Should().HaveLength(DeviceNickname.MaxLength);
    }

    [Theory]
    [InlineData("bell\u0007")]
    [InlineData("null\u0000byte")]
    [InlineData("escape\u001b[31m")]
    public void A_nickname_with_a_control_character_is_refused(string given)
    {
        var act = () => DeviceNickname.Normalize(given);

        act.Should().Throw<ArgumentException>().WithMessage("*control characters*");
    }
}
