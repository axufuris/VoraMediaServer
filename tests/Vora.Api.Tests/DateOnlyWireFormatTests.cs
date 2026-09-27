using System.Text.Json;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.Serialization;
using Vora.Application.Media.ViewModels;
using Vora.Application.Search.ViewModels;

namespace Vora.Api.Tests;

public class DateOnlyWireFormatTests
{
    private static readonly JsonSerializerOptions Options = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new DateOnlyConverter());
        options.Converters.Add(new NullableDateOnlyConverter());
        return options;
    }

    [Fact]
    public void A_release_date_goes_out_as_a_plain_date()
    {
        var json = JsonSerializer.Serialize(new LibraryItemVM { Title = "Alien", ReleaseDate = new DateOnly(1979, 5, 25) }, Options);

        json.Should().Contain("\"releaseDate\":\"1979-05-25\"");
        json.Should().NotContain("1979-05-25T");
    }

    [Fact]
    public void A_new_year_release_keeps_its_year()
    {
        var json = JsonSerializer.Serialize(new LibraryItemVM { Title = "Tenet", ReleaseDate = new DateOnly(2026, 1, 1) }, Options);

        json.Should().Contain("\"releaseDate\":\"2026-01-01\"");
    }

    [Fact]
    public void A_missing_release_date_is_still_null()
    {
        var json = JsonSerializer.Serialize(new LibraryItemVM { Title = "Unknown", ReleaseDate = null }, Options);

        json.Should().Contain("\"releaseDate\":null");
    }

    [Fact]
    public void A_plain_date_is_accepted_back()
    {
        var parsed = JsonSerializer.Deserialize<MediaSearchResultVM>("{\"title\":\"Alien\",\"releaseDate\":\"1979-05-25\"}", Options);

        parsed!.ReleaseDate.Should().Be(new DateOnly(1979, 5, 25));
    }

    [Fact]
    public void An_instant_is_still_accepted_from_an_older_client()
    {
        var parsed = JsonSerializer.Deserialize<MediaSearchResultVM>("{\"title\":\"Alien\",\"releaseDate\":\"1979-05-25T00:00:00Z\"}", Options);

        parsed!.ReleaseDate.Should().Be(new DateOnly(1979, 5, 25));
    }
}
