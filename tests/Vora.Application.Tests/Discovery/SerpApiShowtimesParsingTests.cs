using System.Text.Json;
using Vora.Plugins.Providers.Theaters;

namespace Vora.Application.Tests.Discovery;

public class SerpApiShowtimesParsingTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void A_documented_response_yields_theaters_and_times()
    {
        var showtimes = Parse("""
        [
          {
            "day": "Today",
            "date": "Sep 13",
            "theaters": [
              {
                "name": "AMC Independence Commons 20",
                "address": "19200 East 39th St S, Independence, MO",
                "showing": [
                  { "time": ["1:15pm", "4:30pm"], "type": "Standard" },
                  { "time": ["7:45pm"], "type": "IMAX" }
                ]
              }
            ]
          }
        ]
        """);

        var theaters = SerpApiTheaterProvider.ParseTheaters(showtimes);

        theaters.Should().HaveCount(1);
        theaters[0].Name.Should().Be("AMC Independence Commons 20");
        theaters[0].Address.Should().Be("19200 East 39th St S, Independence, MO");
        theaters[0].Showtimes.Should().HaveCount(3);
        theaters[0].Showtimes.Select(s => s.Time).Should().Equal("1:15pm", "4:30pm", "7:45pm");
    }

    [Fact]
    public void A_format_is_carried_through_from_the_showing_type()
    {
        var showtimes = Parse("""
        [{ "theaters": [{ "name": "AMC", "showing": [{ "time": ["7:45pm"], "type": "Dolby Cinema" }] }] }]
        """);

        SerpApiTheaterProvider.ParseTheaters(showtimes)[0].Showtimes[0].Format.Should().Be("Dolby Cinema");
    }

    [Fact]
    public void A_showing_with_no_type_is_treated_as_standard()
    {
        var showtimes = Parse("""
        [{ "theaters": [{ "name": "AMC", "showing": [{ "time": ["7:45pm"] }] }] }]
        """);

        SerpApiTheaterProvider.ParseTheaters(showtimes)[0].Showtimes[0].Format.Should().Be("Standard");
    }

    [Fact]
    public void A_theater_with_no_times_is_dropped()
    {
        var showtimes = Parse("""
        [{ "theaters": [{ "name": "Closed Cinema", "showing": [] }] }]
        """);

        SerpApiTheaterProvider.ParseTheaters(showtimes).Should().BeEmpty();
    }

    [Fact]
    public void A_theater_with_no_showing_field_is_dropped()
    {
        var showtimes = Parse("""[{ "theaters": [{ "name": "Closed Cinema" }] }]""");

        SerpApiTheaterProvider.ParseTheaters(showtimes).Should().BeEmpty();
    }

    [Fact]
    public void A_missing_name_falls_back_rather_than_throwing()
    {
        var showtimes = Parse("""
        [{ "theaters": [{ "showing": [{ "time": ["7:45pm"] }] }] }]
        """);

        SerpApiTheaterProvider.ParseTheaters(showtimes)[0].Name.Should().Be("Unknown Theater");
    }

    [Fact]
    public void A_missing_address_becomes_empty_rather_than_null()
    {
        var showtimes = Parse("""
        [{ "theaters": [{ "name": "AMC", "showing": [{ "time": ["7:45pm"] }] }] }]
        """);

        SerpApiTheaterProvider.ParseTheaters(showtimes)[0].Address.Should().BeEmpty();
    }

    [Fact]
    public void An_empty_showtimes_array_yields_nothing()
    {
        SerpApiTheaterProvider.ParseTheaters(Parse("[]")).Should().BeEmpty();
    }

    [Fact]
    public void A_day_entry_with_no_theaters_yields_nothing()
    {
        SerpApiTheaterProvider.ParseTheaters(Parse("""[{ "day": "Today", "date": "Sep 13" }]""")).Should().BeEmpty();
    }

    [Fact]
    public void A_blank_time_string_is_skipped()
    {
        var showtimes = Parse("""
        [{ "theaters": [{ "name": "AMC", "showing": [{ "time": ["", "7:45pm"] }] }] }]
        """);

        SerpApiTheaterProvider.ParseTheaters(showtimes)[0].Showtimes.Should().ContainSingle();
    }

    [Fact]
    public void Only_the_first_day_is_read()
    {
        var showtimes = Parse("""
        [
          { "day": "Today", "theaters": [{ "name": "Today Cinema", "showing": [{ "time": ["1:00pm"] }] }] },
          { "day": "Tomorrow", "theaters": [{ "name": "Tomorrow Cinema", "showing": [{ "time": ["2:00pm"] }] }] }
        ]
        """);

        SerpApiTheaterProvider.ParseTheaters(showtimes).Should().ContainSingle()
            .Which.Name.Should().Be("Today Cinema");
    }
}
