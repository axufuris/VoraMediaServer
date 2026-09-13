using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vora.Api.Extensions;

namespace Vora.Api.Tests;

// The calendar failed for every request with "Cannot write DateTime with
// Kind=Unspecified to PostgreSQL type 'timestamp with time zone'". The dates
// reach the repository exactly as bound from the query string, so what minimal
// API binding produces for a given format decides whether the query works at
// all. These pin that down rather than leaving it to be assumed.
public class CalendarDateBindingTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services => services.AddRouting())
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/bound", ([FromQuery] DateTime value) => Microsoft.AspNetCore.Http.Results.Ok(value.Kind.ToString()));
                        endpoints.MapGet("/normalized", ([FromQuery] DateTime value) => Microsoft.AspNetCore.Http.Results.Ok(value.AsUtc().Kind.ToString()));
                    });
                }))
            .StartAsync();

        _client = _host.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    private async Task<string> BoundKindAsync(string value) =>
        await _client.GetStringAsync($"/bound?value={Uri.EscapeDataString(value)}");

    // What the web client sends via Date.toISOString(). Binding honours the
    // zone, which is why the web calendar was never the caller that broke.
    [Fact]
    public async Task An_iso_string_with_a_zone_binds_as_utc()
    {
        var kind = await BoundKindAsync("2026-09-06T00:00:00.000Z");

        kind.Should().Contain("Utc");
    }

    [Fact]
    public async Task An_offset_is_resolved_to_utc()
    {
        var kind = await BoundKindAsync("2026-09-06T00:00:00-05:00");

        kind.Should().Contain("Utc");
    }

    // A date with no zone at all, which is what a plain YYYY-MM-DD gives.
    [Fact]
    public async Task A_bare_date_binds_with_no_kind_at_all()
    {
        var kind = await BoundKindAsync("2026-09-06");

        kind.Should().Contain("Unspecified");
    }

    [Fact]
    public async Task A_local_looking_timestamp_binds_with_no_kind_at_all()
    {
        var kind = await BoundKindAsync("2026-09-06T00:00:00");

        kind.Should().Contain("Unspecified");
    }

    [Theory]
    [InlineData("2026-09-06")]
    [InlineData("2026-09-06T00:00:00")]
    [InlineData("2026-09-06T00:00:00.000Z")]
    [InlineData("2026-09-06T00:00:00-05:00")]
    public async Task Every_format_a_client_might_send_survives_the_boundary(string value)
    {
        var kind = await _client.GetStringAsync($"/normalized?value={Uri.EscapeDataString(value)}");

        kind.Should().Contain("Utc");
    }
}
