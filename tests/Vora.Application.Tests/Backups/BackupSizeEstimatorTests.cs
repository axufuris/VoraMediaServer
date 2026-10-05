using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Backups;

namespace Vora.Application.Tests.Backups;

public class BackupSizeEstimatorTests
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record HistoryRow(Guid Id, Guid ProfileId, Guid MediaItemId, double ResumePositionSeconds, bool IsPlayed, DateTime LastPlayedAt);

    private sealed class RowsSection : IBackupSection
    {
        private readonly int _rows;

        public RowsSection(string key, int rows)
        {
            Key = key;
            _rows = rows;
        }

        public int Writes { get; private set; }
        public string Key { get; }
        public string DisplayName => Key;
        public BackupSectionGroup Group => BackupSectionGroup.UserData;
        public bool RequiresExplicitConfirm => false;
        public bool CanGrowLarge => false;
        public string? DestructiveWarning => null;

        public async Task WriteAsync(IBackupWriter writer, CancellationToken ct)
        {
            Writes++;
            var random = new Random(_rows);
            var rows = Enumerable.Range(0, _rows)
                .Select(i => new HistoryRow(
                    new Guid(i, 0, 0, new byte[8]),
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    new Guid(random.Next(), (short)i, 7, new byte[8]),
                    random.Next(0, 7200),
                    i % 3 == 0,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(random.Next(0, 400000))))
                .ToList();
            await writer.WriteJsonAsync($"{Key}/rows.json", rows, ct);
            await writer.WriteJsonAsync(BackupIdentityFile.PathFor(Key), new BackupIdentityFile
            {
                MediaItems = rows.Select(r => new BackupItemIdentity { Id = r.MediaItemId, Keys = { $"movie:tmdb:{r.MediaItemId}" } }).ToList()
            }, ct);
        }

        public Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct) =>
            Task.FromResult(new BackupSectionImportResult());
    }

    private sealed class FailingSection : IBackupSection
    {
        public string Key => "broken";
        public string DisplayName => "Broken";
        public BackupSectionGroup Group => BackupSectionGroup.Settings;
        public bool RequiresExplicitConfirm => false;
        public bool CanGrowLarge => false;
        public string? DestructiveWarning => null;

        public Task WriteAsync(IBackupWriter writer, CancellationToken ct) => throw new InvalidOperationException("table missing");

        public Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct) =>
            Task.FromResult(new BackupSectionImportResult());
    }

    private readonly ManualTimeProvider _clock = new();
    private readonly RowsSection _history = new("users.watch-history", 3000);
    private readonly RowsSection _settings = new("settings.server", 1);

    private BackupSizeEstimator NewEstimator(params IBackupSection[] extra)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBackupSection>(_settings);
        services.AddSingleton<IBackupSection>(_history);
        foreach (var section in extra)
        {
            services.AddSingleton(section);
        }
        var provider = services.BuildServiceProvider();
        return new BackupSizeEstimator(provider.GetRequiredService<IServiceScopeFactory>(), _clock, NullLogger<BackupSizeEstimator>.Instance);
    }

    [Fact]
    public async Task Each_section_gets_its_compressed_size_and_row_count()
    {
        var estimate = await NewEstimator().EstimateAsync(refresh: false, TestContext.Current.CancellationToken);

        estimate.Sections.Select(s => s.Key).Should().Equal("settings.server", "users.watch-history");
        var history = estimate.Sections.Single(s => s.Key == "users.watch-history");
        history.RowCount.Should().Be(3000);
        history.EstimatedBytes.Should().BeGreaterThan(estimate.Sections.Single(s => s.Key == "settings.server").EstimatedBytes);
        estimate.OverheadBytes.Should().BeGreaterThan(0);
        estimate.EstimatedAtUtc.Should().Be(_clock.Now.UtcDateTime);
    }

    [Fact]
    public async Task The_estimate_matches_the_size_of_a_real_backup_zip()
    {
        var estimate = await NewEstimator().EstimateAsync(refresh: false, TestContext.Current.CancellationToken);

        using var zip = new MemoryStream();
        var manifest = new BackupManifest { CreatedAtUtc = _clock.Now.UtcDateTime };
        using (var writer = new ZipBackupWriter(zip))
        {
            foreach (var section in new IBackupSection[] { _settings, _history })
            {
                writer.BeginSection(section.Key);
                await section.WriteAsync(writer, TestContext.Current.CancellationToken);
                manifest.Sections.Add(new BackupSectionManifestEntry
                {
                    Key = section.Key,
                    DisplayName = section.DisplayName,
                    Group = section.Group.ToString(),
                    SizeBytes = await writer.GetSectionSizeAsync(TestContext.Current.CancellationToken),
                    ItemCount = writer.GetSectionRowCount()
                });
                writer.EndSection();
            }
            await writer.WriteManifestAsync(manifest, TestContext.Current.CancellationToken);
        }

        var estimated = estimate.OverheadBytes + estimate.Sections.Sum(s => s.EstimatedBytes);
        ((double)estimated).Should().BeApproximately(zip.Length, zip.Length * 0.02);
        manifest.Sections.Single(s => s.Key == "users.watch-history").ItemCount.Should().Be(3000);
    }

    [Fact]
    public async Task A_section_that_cannot_be_estimated_is_flagged_without_failing_the_rest()
    {
        var estimate = await NewEstimator(new FailingSection()).EstimateAsync(refresh: false, TestContext.Current.CancellationToken);

        estimate.Sections.Single(s => s.Key == "broken").Failed.Should().BeTrue();
        estimate.Sections.Single(s => s.Key == "users.watch-history").Failed.Should().BeFalse();
    }

    [Fact]
    public async Task Estimates_are_cached_for_a_few_minutes_unless_refreshed_or_invalidated()
    {
        var estimator = NewEstimator();
        var ct = TestContext.Current.CancellationToken;

        var first = await estimator.EstimateAsync(refresh: false, ct);
        _clock.Now = _clock.Now.AddMinutes(4);
        var cached = await estimator.EstimateAsync(refresh: false, ct);
        _history.Writes.Should().Be(1);
        cached.Should().BeSameAs(first);

        await estimator.EstimateAsync(refresh: true, ct);
        _history.Writes.Should().Be(2);

        estimator.Invalidate();
        await estimator.EstimateAsync(refresh: false, ct);
        _history.Writes.Should().Be(3);

        _clock.Now = _clock.Now.Add(BackupSizeEstimator.CacheDuration);
        await estimator.EstimateAsync(refresh: false, ct);
        _history.Writes.Should().Be(4);
    }
}
