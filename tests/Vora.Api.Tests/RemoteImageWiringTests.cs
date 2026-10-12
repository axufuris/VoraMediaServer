using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vora.Api.Tests.Infra;
using Vora.Application.Artwork;
using Vora.Application.Iptv.ViewModels;

namespace Vora.Api.Tests;

public class RemoteImageWiringTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public RemoteImageWiringTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void A_logo_written_by_an_endpoint_becomes_one_the_image_cache_will_fetch()
    {
        const string logo = "http://www.181.fm/images/wiring-test.png";
        var json = _factory.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        var registry = _factory.Services.GetRequiredService<IRemoteImageRegistry>();
        registry.IsKnown(logo).Should().BeFalse();

        var written = JsonSerializer.Serialize(new IptvChannelVM { Name = "181.FM", LogoUrl = logo }, json);

        written.Should().Contain("\"logoUrl\":\"http://www.181.fm/images/wiring-test.png\"");
        registry.IsKnown(logo).Should().BeTrue();
    }

    [Fact]
    public void Controllers_write_through_the_same_registry()
    {
        const string logo = "http://www.181.fm/images/wiring-test-mvc.png";
        var json = _factory.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;

        JsonSerializer.Serialize(new IptvChannelVM { Name = "181.FM", LogoUrl = logo }, json);

        _factory.Services.GetRequiredService<IRemoteImageRegistry>().IsKnown(logo).Should().BeTrue();
    }
}
