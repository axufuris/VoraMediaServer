using Microsoft.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class MusicResumeRepositoryTests
{
    private readonly string _name = "music-resume-" + Guid.NewGuid().ToString("N");
    private readonly Guid _profile = Guid.NewGuid();

    private VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>().UseInMemoryDatabase(_name).Options);

    private async Task SaveAsync(MusicPlaySource source, Guid sourceId, int index, params Guid[] ids)
    {
        await using var db = NewContext();
        await new MusicResumeRepository(db).SaveAsync(_profile, source, sourceId, ids, index);
    }

    private async Task<List<Domain.Entities.Media.MusicResumePoint>> AllAsync()
    {
        await using var db = NewContext();
        return await new MusicResumeRepository(db).GetAsync(_profile, 100);
    }

    [Fact]
    public async Task Saving_the_same_source_again_updates_its_point_instead_of_adding_another()
    {
        var source = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await SaveAsync(MusicPlaySource.Playlist, source, 0, a, b);
        await SaveAsync(MusicPlaySource.Playlist, source, 1, a, b);

        var point = (await AllAsync()).Should().ContainSingle().Subject;
        point.TrackIndex.Should().Be(1);
        point.TrackIds.Should().Equal(a, b);
    }

    [Fact]
    public async Task A_playlist_and_a_mix_with_the_same_id_are_two_points()
    {
        var id = Guid.NewGuid();

        await SaveAsync(MusicPlaySource.Playlist, id, 0, Guid.NewGuid());
        await SaveAsync(MusicPlaySource.Mix, id, 0, Guid.NewGuid());

        (await AllAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Points_come_back_newest_first_and_the_limit_cuts_the_list()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        await SaveAsync(MusicPlaySource.Playlist, first, 0, Guid.NewGuid());
        await Task.Delay(5, TestContext.Current.CancellationToken);
        await SaveAsync(MusicPlaySource.Playlist, second, 0, Guid.NewGuid());
        await Task.Delay(5, TestContext.Current.CancellationToken);
        await SaveAsync(MusicPlaySource.Playlist, third, 0, Guid.NewGuid());
        await Task.Delay(5, TestContext.Current.CancellationToken);
        await SaveAsync(MusicPlaySource.Playlist, first, 0, Guid.NewGuid());

        (await AllAsync()).Select(p => p.SourceId).Should().Equal(first, third, second);
        await using var db = NewContext();
        (await new MusicResumeRepository(db).GetAsync(_profile, 2)).Select(p => p.SourceId).Should().Equal(first, third);
    }

    [Fact]
    public async Task Only_the_newest_points_are_kept()
    {
        var sources = Enumerable.Range(0, MusicResumeLimits.MaxPoints + 3).Select(_ => Guid.NewGuid()).ToList();
        foreach (var source in sources)
        {
            await SaveAsync(MusicPlaySource.Playlist, source, 0, Guid.NewGuid());
            await Task.Delay(3, TestContext.Current.CancellationToken);
        }

        var kept = await AllAsync();

        kept.Should().HaveCount(MusicResumeLimits.MaxPoints);
        kept.Select(p => p.SourceId).Should().Equal(Enumerable.Reverse(sources).Take(MusicResumeLimits.MaxPoints));
    }

    [Fact]
    public async Task Points_belong_to_their_profile()
    {
        await SaveAsync(MusicPlaySource.None, Guid.Empty, 0, Guid.NewGuid());
        await using var db = NewContext();
        var repository = new MusicResumeRepository(db);

        (await repository.GetAsync(Guid.NewGuid(), 10)).Should().BeEmpty();
        (await repository.GetAsync(Guid.NewGuid(), MusicPlaySource.None, Guid.Empty)).Should().BeNull();
        (await repository.GetAsync(_profile, MusicPlaySource.None, Guid.Empty)).Should().NotBeNull();
    }

    [Fact]
    public async Task Removing_a_point_leaves_the_others_and_removing_a_missing_one_is_fine()
    {
        var keep = Guid.NewGuid();
        var drop = Guid.NewGuid();
        await SaveAsync(MusicPlaySource.Playlist, keep, 0, Guid.NewGuid());
        await SaveAsync(MusicPlaySource.Playlist, drop, 0, Guid.NewGuid());

        await using (var db = NewContext())
        {
            var repository = new MusicResumeRepository(db);
            await repository.RemoveAsync(_profile, MusicPlaySource.Playlist, drop);
            await repository.RemoveAsync(_profile, MusicPlaySource.Playlist, drop);
        }

        (await AllAsync()).Select(p => p.SourceId).Should().Equal(keep);
    }
}
