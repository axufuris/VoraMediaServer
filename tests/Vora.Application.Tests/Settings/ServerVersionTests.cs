using Vora.Application.Settings;

namespace Vora.Application.Tests.Settings;

public class ServerVersionTests
{
    [Fact]
    public void A_release_build_reports_its_tag_and_commit()
    {
        var version = ServerVersion.Parse("0.1.0-beta.1+9fceb02d8f3ac1b0ecc2b1e1a6b3a7a1c0d4e5f6");

        version.Version.Should().Be("0.1.0-beta.1");
        version.Commit.Should().Be("9fceb02d8f3ac1b0ecc2b1e1a6b3a7a1c0d4e5f6");
        version.IsPrerelease.Should().BeTrue();
    }

    [Fact]
    public void A_stable_version_is_not_flagged_as_a_prerelease()
    {
        var version = ServerVersion.Parse("1.2.3");

        version.Version.Should().Be("1.2.3");
        version.Commit.Should().BeNull();
        version.IsPrerelease.Should().BeFalse();
    }

    [Fact]
    public void A_local_build_reports_no_commit()
    {
        ServerVersion.Parse("0.1.0-beta.1+local").Commit.Should().BeNull();
        ServerVersion.Parse("0.1.0-beta.1+").Commit.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unstamped_assembly_reads_as_unknown(string? informationalVersion)
    {
        var version = ServerVersion.Parse(informationalVersion);

        version.Version.Should().Be("unknown");
        version.IsPrerelease.Should().BeFalse();
    }

    [Fact]
    public void The_running_assembly_reports_a_real_version()
    {
        var version = ServerVersion.Read(typeof(ServerVersion).Assembly);

        version.Version.Should().NotBe("unknown");
        version.Version.Should().StartWith("0.");
    }
}
