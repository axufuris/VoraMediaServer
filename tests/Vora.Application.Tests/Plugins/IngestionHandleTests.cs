using Vora.Plugins.Dtos;

namespace Vora.Application.Tests.Plugins;

public class IngestionHandleTests
{
    [Fact]
    public void LibraryHandle_FromGuid_round_trips_the_value()
    {
        var id = Guid.NewGuid();

        var handle = LibraryHandle.FromGuid(id);

        handle.Value.Should().Be(id);
    }

    [Fact]
    public void LibraryHandle_equality_is_value_based()
    {
        var id = Guid.NewGuid();

        var a = LibraryHandle.FromGuid(id);
        var b = LibraryHandle.FromGuid(id);

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void LibraryHandle_with_different_ids_are_not_equal()
    {
        var a = LibraryHandle.FromGuid(Guid.NewGuid());
        var b = LibraryHandle.FromGuid(Guid.NewGuid());

        a.Should().NotBe(b);
    }

    [Fact]
    public void MediaItemHandle_uses_internal_constructor()
    {
        var id = Guid.NewGuid();

        var handle = new MediaItemHandle(id);

        handle.Value.Should().Be(id);
    }

    [Fact]
    public void Handle_types_are_distinct_at_compile_time()
    {
        var library = LibraryHandle.FromGuid(Guid.NewGuid());
        var item = new MediaItemHandle(Guid.NewGuid());
        var season = new SeasonHandle(Guid.NewGuid());
        var artist = new ArtistHandle(Guid.NewGuid());
        var album = new AlbumHandle(Guid.NewGuid());

        library.GetType().Should().NotBe(item.GetType());
        item.GetType().Should().NotBe(season.GetType());
        artist.GetType().Should().NotBe(album.GetType());
    }
}
