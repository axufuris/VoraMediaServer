using Vora.Application.Backups;

namespace Vora.Application.Tests.Backups;

public class BackupSkipTallyTests
{
    private static readonly BackupRowNoun Rows = new("watch-history row", "watch-history rows");

    [Fact]
    public void Skips_are_counted_and_described_in_plain_english()
    {
        var tally = new BackupSkipTally();
        tally.Skip(Rows, BackupSkipReason.MissingItem, 36);
        tally.Skip(Rows, BackupSkipReason.MissingItem);
        tally.Skip(Rows, BackupSkipReason.MissingProfile);

        var result = tally.ToResult(100);

        result.RowsImported.Should().Be(100);
        result.RowsSkipped.Should().Be(38);
        result.Warnings.Should().HaveCount(2);
        result.Warnings[0].Should().StartWith("37 watch-history rows were skipped because their item isn't on this server.");
        result.Warnings[1].Should().StartWith("1 watch-history row was skipped because its profile isn't on this server.");
    }

    [Fact]
    public void Nothing_skipped_means_no_warnings()
    {
        var tally = new BackupSkipTally();
        tally.Skip(Rows, BackupSkipReason.Duplicate, 0);

        tally.ToResult(5).Warnings.Should().BeEmpty();
        tally.Total.Should().Be(0);
    }

    [Fact]
    public void Large_counts_use_thousands_separators()
    {
        BackupSkipTally.Describe(Rows, BackupSkipReason.Duplicate, 12345)
            .Should().Be("12,345 watch-history rows were dropped because they matched the same item as another row on this server.");
    }
}
